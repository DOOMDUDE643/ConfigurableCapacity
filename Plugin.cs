using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using HutongGames.PlayMaker;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace ConfigurableCapacity
{
    // How the game works (read from the FSM logic dump):
    //  * FSM "Ammo" on __GameManager__ clamps each reserve count to ammo_<type>_capacity EVERY frame.
    //  * FSM "Backpack" (same object) re-writes those capacities whenever a backpack is equipped / a save loads:
    //        Ammo.ammo_<type>_capacity = <hard-coded vanilla number> x multiply     (huge backpack: multiply = 3)
    //
    // This plugin:
    //  1. Before each Update of the "Ammo" FSM, raises every capacity to >= vanilla x Multiplier, so the
    //     per-frame clamp never trims your ammo.
    //  2. (StackWithBackpack) edits the hard-coded numbers inside the Backpack FSM's "SetIntValue" actions
    //     (40 -> 40 x Multiplier ...), so the game itself computes  vanilla x Multiplier x backpack bonus.
    // Each ammo type can have its own multiplier (config section [AmmoTypes]); 0 = use CapacityMultiplier.
    // Magazine sizes (ammo_in_gun) are never touched.
    //
    // Formerly "Apocalypter Ammo Tool" (GUID com.Thegerman643.apocalypter.ammotool). Its config is carried over once.
    [BepInPlugin(Plugin.PluginGuid, "Configurable Capacity", Plugin.PluginVersion)]
    [BepInIncompatibility("com.Thegerman643.apocalypter.ammotool")]
    [BepInIncompatibility("com.yourname.apocalypter.ammotool")]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.Thegerman643.apocalypter.configurablecapacity";
        public const string PluginVersion = "1.0.0";
        const string OldGuid = "com.Thegerman643.apocalypter.ammotool";

        internal static Plugin Instance;

        ConfigEntry<float> multiplier;
        ConfigEntry<string> fsmNames, objectName;
        ConfigEntry<bool> stackWithBackpack, verbose;
        readonly Dictionary<string, ConfigEntry<float>> typeMult = new Dictionary<string, ConfigEntry<float>>(StringComparer.OrdinalIgnoreCase);
        ConfigEntry<KeyCode> dumpKey, logicKey;
        HashSet<string> targetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        static readonly Dictionary<string, int> Vanilla = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "ammo_22caliber_capacity", 60 }, { "ammo_9mm_capacity", 40 }, { "ammo_20gauge_capacity", 25 },
            { "ammo_arrow_capacity", 20 },     { "ammo_battery_capacity", 6 }, { "ammo_grenade_capacity", 2 },
            { "ammo_12gauge_capacity", 20 },   { "ammo_556_capacity", 30 },    { "ammo_762_capacity", 30 },
            { "ammo_3006_capacity", 15 }
        };

        sealed class Cap { public FsmInt V; public int Target; }
        // One of the (at most a few) FSMs that matter. Kind 1 = Ammo FSM (raise capacities), Kind 2 = Backpack FSM (edit its hard-coded limits).
        sealed class Target { public PlayMakerFSM F; public byte Kind; public Cap[] Caps; }

        Target[] targets = new Target[0];
        readonly HashSet<object> patchedActions = new HashSet<object>();
        readonly Dictionary<string, int> learned = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, int> faultCounts = new Dictionary<string, int>();

        int lastFrame = -1, changesLogged, patchesLogged, missCalls;
        bool active, inert, checkedOld, legacyBroken, newBroken, migrated;
        float nextPatch, nextFind;

        // BepInEx has already created Config here, but nothing has been bound yet: the right moment to carry over the old mod's settings.
        public Plugin()
        {
            try { MigrateOldConfig(); } catch { }
        }

        // Earlier builds were called "Apocalypter Ammo Tool". Copy its settings file over once so nobody has to set everything up again.
        void MigrateOldConfig()
        {
            string target = Path.Combine(Paths.ConfigPath, PluginGuid + ".cfg");
            string old = Path.Combine(Paths.ConfigPath, OldGuid + ".cfg");
            if (File.Exists(target) || !File.Exists(old)) return;
            File.Copy(old, target);
            Config.Reload();
            migrated = true;
        }

        void Awake()
        {
            Instance = this;

            multiplier = Config.Bind("General", "CapacityMultiplier", 3f,
                "How many times the game's own reserve ammo limit you can carry (3 = triple). 1 = vanilla: the mod does nothing. Restart the game after changing it.");
            stackWithBackpack = Config.Bind("General", "StackWithBackpack", true,
                "On: your backpack's own bonus is multiplied as well (for example a huge backpack's x3 AND this multiplier). Off: you get at least the game's base limit times the multiplier, but the backpack bonus is not multiplied on top.");
            Config.Bind("General", "Apocasetter", true, "Show this mod in the Apocasetter Mods menu.");

            BindType("ammo_22caliber_capacity", "22caliber", ".22 caliber");
            BindType("ammo_9mm_capacity", "9mm", "9mm");
            BindType("ammo_20gauge_capacity", "20gauge", "20 gauge");
            BindType("ammo_12gauge_capacity", "12gauge", "12 gauge");
            BindType("ammo_556_capacity", "556mm", "5.56mm");
            BindType("ammo_762_capacity", "762mm", "7.62mm");
            BindType("ammo_3006_capacity", "3006", ".30-06");
            BindType("ammo_arrow_capacity", "arrows", "arrows / bolts");
            BindType("ammo_battery_capacity", "batteries", "batteries");
            BindType("ammo_grenade_capacity", "grenades", "grenades");

            fsmNames = Config.Bind("Advanced", "FsmNames", "Ammo",
                "Advanced: the game's ammo FSM(s) on the game manager object whose ammo_*_capacity variables get raised (comma separated). Only change this if a game update renames them.");
            objectName = Config.Bind("Advanced", "ObjectName", "__GameManager__",
                "Advanced: only FSMs on a game object with exactly this name are changed (this keeps ammo boxes and other items untouched). Only change this if a game update renames it.");

            verbose = Config.Bind("Developer", "VerboseLog", false, "Write detailed diagnostic lines to the BepInEx log. Leave off for normal play.");
            dumpKey = Config.Bind("Developer", "DumpVariablesHotkey", KeyCode.None, "A key that writes BepInEx/ammo_dump_key.txt with every ammo-related PlayMaker variable (diagnostics).");
            logicKey = Config.Bind("Developer", "DumpLogicHotkey", KeyCode.None, "A key that writes BepInEx/ammo_logic.txt with the states and actions of the ammo-related FSMs (diagnostics).");

            ParseNames();
            active = ComputeActive();
            multiplier.SettingChanged += (s, e) => SettingsChanged();
            foreach (var kv in typeMult) kv.Value.SettingChanged += (s, e) => SettingsChanged();
            stackWithBackpack.SettingChanged += (s, e) => { SettingsChanged(); FindTargets(true); };
            fsmNames.SettingChanged += (s, e) => { ParseNames(); SettingsChanged(); FindTargets(true); };
            objectName.SettingChanged += (s, e) => { SettingsChanged(); FindTargets(true); };

            Logger.LogInfo("Configurable Capacity " + PluginVersion + " loaded (reserve limits x" + multiplier.Value + (migrated ? "; settings carried over from the old Apocalypter Ammo Tool, you can delete ApocalypterAmmoTool.dll" : "") + ").");
            Run(InstallHooks, "install hooks");
        }

        void BindType(string varName, string key, string label)
        {
            typeMult[varName] = Config.Bind("AmmoTypes", key, 0f,
                "Multiplier for the " + label + " reserve limit. 0 = use CapacityMultiplier. 1 = vanilla (no change). Restart the game after changing it.");
        }

        // Effective multiplier for one capacity variable (never below 1: this mod only raises limits).
        float MultFor(string varName)
        {
            ConfigEntry<float> e;
            float m = (varName != null && typeMult.TryGetValue(varName, out e) && e.Value > 0f) ? e.Value : multiplier.Value;
            return Mathf.Clamp(m, 1f, 1000f);
        }

        bool ComputeActive()
        {
            if (multiplier.Value > 1f) return true;
            foreach (var kv in typeMult) if (kv.Value.Value > 1f) return true;
            return false;
        }

        void SettingsChanged()
        {
            active = ComputeActive();
            foreach (var t in targets) t.Caps = null;   // rebuilt on the next frame with the new multipliers
        }

        void ParseNames()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in fsmNames.Value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)) set.Add(n.Trim());
            targetNames = set;
        }

        // ---------------- Harmony hooks into PlayMaker ----------------
        [MethodImpl(MethodImplOptions.NoInlining)]
        void InstallHooks()
        {
            var harmony = new Harmony(PluginGuid);
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            var pre = new HarmonyMethod(typeof(Hooks).GetMethod("BeforeUpdate", BindingFlags.Static | BindingFlags.Public));
            var post = new HarmonyMethod(typeof(Hooks).GetMethod("AfterStart", BindingFlags.Static | BindingFlags.Public));

            int hooked = 0;
            // Update: PREFIX, so capacities are raised before the FSM's own per-frame IntClamp runs.
            hooked += TryHook(harmony, "Update", flags, pre, null);
            hooked += TryHook(harmony, "Start", flags, null, post);
            hooked += TryHook(harmony, "OnEnable", flags, null, post);

            if (hooked == 0)
            {
                Logger.LogError("Could not hook any PlayMaker method, so the mod cannot work. PlayMakerFSM methods are:");
                foreach (var m in typeof(PlayMakerFSM).GetMethods(flags)) Logger.LogError("   PlayMakerFSM." + m.Name + "(" + m.GetParameters().Length + ")");
            }
            else Say(hooked + " PlayMaker hook(s) installed.");
        }

        int TryHook(Harmony harmony, string name, BindingFlags flags, HarmonyMethod prefix, HarmonyMethod postfix)
        {
            try
            {
                MethodInfo m = null;
                foreach (var cand in typeof(PlayMakerFSM).GetMethods(flags))
                    if (cand.Name == name && cand.GetParameters().Length == 0 && !cand.IsAbstract && !cand.IsGenericMethod) { m = cand; break; }
                if (m == null) return 0;
                harmony.Patch(m, prefix: prefix, postfix: postfix);
                Say("hooked PlayMakerFSM." + name + (prefix != null ? " (before)" : " (after)"));
                return 1;
            }
            catch (Exception e)
            {
                Logger.LogWarning("Hooking PlayMakerFSM." + name + " failed: " + e.Message);
                return 0;
            }
        }

        // Called for EVERY FSM, EVERY frame (about 2,000 times a frame): it must stay a reference comparison against a tiny array.
        internal void OnUpdate(PlayMakerFSM f)
        {
            if (!checkedOld) CheckOldPlugin();
            var t = targets;
            for (int i = 0; i < t.Length; i++)
            {
                var tg = t[i];
                if (!ReferenceEquals(tg.F, f)) continue;
                Tick();                                   // once per frame (the Ammo FSM updates every frame while a game is running)
                if (inert || !active) return;
                if (tg.Kind == 1) RaiseCapacities(tg); else PatchBackpack(tg);
                return;
            }
            // Not one of ours. If we are not tracking anything yet (hooks installed after the game manager started), look now and then.
            if (t.Length == 0 && !inert && (++missCalls & 1023) == 0) FindTargets(false);
        }

        // Start / OnEnable of any FSM: cheap rejection first (name check on a managed string), then look for the game manager's FSMs.
        internal void OnStarted(PlayMakerFSM f)
        {
            if (f == null || inert) return;
            string nm = f.FsmName;
            if (nm == null || !(targetNames.Contains(nm) || string.Equals(nm, "Backpack", StringComparison.OrdinalIgnoreCase))) return;
            if (!string.Equals(f.gameObject.name, objectName.Value, StringComparison.Ordinal)) return;
            FindTargets(true);
        }

        // Once, on the first FSM update (every plugin is loaded by then): if the old "Apocalypter Ammo Tool" is still installed it would multiply limits a second time.
        void CheckOldPlugin()
        {
            checkedOld = true;
            try
            {
                if (BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(OldGuid))
                {
                    inert = true;
                    Logger.LogWarning("The old 'Apocalypter Ammo Tool' is still installed, so Configurable Capacity is switched off to avoid raising limits twice. Delete ApocalypterAmmoTool.dll (your settings are already carried over).");
                }
            }
            catch (Exception e) { Fault("old plugin check", e); }
        }

        // Find the FSMs on the game manager object that we change.
        void FindTargets(bool force)
        {
            float now = Time.realtimeSinceStartup;
            if (!force && now < nextFind) return;
            nextFind = now + 2f;

            var list = new List<Target>();
            var go = GameObject.Find(objectName.Value);
            if (go != null)
                foreach (var f in go.GetComponents<PlayMakerFSM>())
                {
                    string nm = f == null ? null : f.FsmName;
                    if (nm == null) continue;
                    byte kind = 0;
                    if (stackWithBackpack.Value && string.Equals(nm, "Backpack", StringComparison.OrdinalIgnoreCase)) kind = 2;
                    else if (targetNames.Contains(nm)) kind = 1;
                    if (kind != 0) list.Add(new Target { F = f, Kind = kind });
                }
            targets = list.ToArray();
            if (list.Count > 0) Say("tracking " + list.Count + " FSM(s) on '" + objectName.Value + "'.");
        }

        static bool IsCapName(string n)
        {
            return n != null && n.Length >= 15 &&
                   n.StartsWith("ammo_", StringComparison.OrdinalIgnoreCase) &&
                   n.EndsWith("_capacity", StringComparison.OrdinalIgnoreCase);
        }

        // ---- 1. keep the Ammo FSM's capacities >= vanilla x Multiplier (runs before the FSM's own clamp) ----
        void RaiseCapacities(Target tg)
        {
            var f = tg.F;
            if (f == null) return;
            var caps = tg.Caps;
            if (caps == null)
            {
                caps = BuildCaps(f.Fsm);
                if (caps.Length == 0) return; // variables not ready yet; try again next frame
                tg.Caps = caps;
                Say("tracking " + caps.Length + " reserve-capacity variables in FSM '" + f.FsmName + "'.");
            }
            foreach (var c in caps)
            {
                if (c.V.Value >= c.Target) continue;
                int old = c.V.Value;
                c.V.Value = c.Target;
                if (verbose.Value && changesLogged++ < 60)
                    Logger.LogInfo(string.Format("{0}/{1}: {2} {3} -> {4}", f.gameObject.name, f.FsmName, c.V.Name, old, c.Target));
            }
        }

        Cap[] BuildCaps(Fsm fsm)
        {
            var list = new List<Cap>();
            if (fsm != null && fsm.Variables != null)
                foreach (var v in fsm.Variables.IntVariables)
                {
                    if (v == null || !IsCapName(v.Name)) continue;
                    int baseVal;
                    if (!Vanilla.TryGetValue(v.Name, out baseVal) && !learned.TryGetValue(v.Name, out baseVal))
                    {
                        if (v.Value <= 0) continue;
                        baseVal = v.Value; learned[v.Name] = baseVal; // unknown (modded) ammo type
                    }
                    list.Add(new Cap { V = v, Target = Mathf.RoundToInt(baseVal * MultFor(v.Name)) });
                }
            return list.ToArray();
        }

        // ---- 2. edit the hard-coded numbers in the Backpack FSM's SetIntValue actions ----
        [MethodImpl(MethodImplOptions.NoInlining)]
        void PatchBackpack(Target tg)
        {
            var f = tg.F;
            if (f == null) return;
            float now = Time.realtimeSinceStartup;
            if (now < nextPatch) return;
            nextPatch = now + 1f;

            var states = Member(f.Fsm, "States") as IEnumerable;
            if (states == null) return;
            int patched = 0;
            foreach (var st in states)
            {
                var acts = Member(st, "Actions") as IEnumerable;
                if (acts == null) continue;
                foreach (var a in acts)
                {
                    if (a == null || a.GetType().Name != "SetIntValue" || patchedActions.Contains(a)) continue;
                    var target = Member(a, "intVariable") as NamedVariable;
                    var literal = Member(a, "intValue") as FsmInt;
                    if (target == null || literal == null || !IsCapName(target.Name)) continue;

                    int old = literal.Value;
                    literal.Value = Mathf.RoundToInt(old * MultFor(target.Name));
                    patchedActions.Add(a);
                    patched++;
                    if (verbose.Value && patchesLogged++ < 40) Logger.LogInfo("Backpack FSM: " + target.Name + " base " + old + " -> " + literal.Value);
                }
            }
            if (patched > 0)
            {
                Say("patched " + patched + " hard-coded limits in the Backpack FSM.");
                // If the FSM already ran its "set" state with the old numbers, bounce it once so the game
                // recomputes the limits (this is the same event the FSM sends itself when the backpack changes).
                if (f.Fsm.ActiveStateName == "set")
                {
                    f.SendEvent("back");
                    Say("asked the Backpack FSM to recompute the limits.");
                }
            }
        }

        // ---------------- developer hotkeys (off by default) ----------------
        void Tick()
        {
            if (Time.frameCount == lastFrame) return;
            lastFrame = Time.frameCount;
            try
            {
                if (dumpKey.Value != KeyCode.None && KeyPressed(dumpKey.Value)) Run(DumpNow, "dump");
                if (logicKey.Value != KeyCode.None && KeyPressed(logicKey.Value)) Run(DumpLogic, "logic dump");
            }
            catch (Exception e) { Fault("tick", e); }
        }

        // ---------------- diagnostics ----------------
        static bool IsAmmoName(string n)
        {
            return n.IndexOf("ammo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   n.IndexOf("capacity", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   n.IndexOf("backpack", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   n.IndexOf("multiply", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        void DumpNow()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Scene: " + SceneManager.GetActiveScene().name + "   time: " + DateTime.Now.ToString("HH:mm:ss"));
            foreach (var f in UnityEngine.Object.FindObjectsOfType<PlayMakerFSM>(true))
            {
                if (f == null || f.Fsm == null || f.Fsm.Variables == null) continue;
                var lines = new List<string>();
                foreach (var v in f.Fsm.Variables.IntVariables)
                    if (v != null && v.Name != null && IsAmmoName(v.Name)) lines.Add("    int " + v.Name + " = " + v.Value);
                foreach (var v in f.Fsm.Variables.FloatVariables)
                    if (v != null && v.Name != null && IsAmmoName(v.Name)) lines.Add("    float " + v.Name + " = " + v.Value);
                if (lines.Count == 0) continue;
                sb.AppendLine("[" + f.gameObject.name + "]  FSM '" + f.FsmName + "'");
                foreach (var l in lines) sb.AppendLine(l);
            }
            string path = Path.Combine(Paths.BepInExRootPath, "ammo_dump_key.txt");
            File.WriteAllText(path, sb.ToString());
            Logger.LogInfo("Wrote " + path);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        void DumpLogic()
        {
            var sb = new StringBuilder();
            sb.AppendLine("FSM logic dump   time: " + DateTime.Now.ToString("HH:mm:ss"));
            int boxes = 0, total = 0;
            bool reload = false;
            foreach (var f in UnityEngine.Object.FindObjectsOfType<PlayMakerFSM>(true))
            {
                if (f == null || f.Fsm == null) continue;
                string go = f.gameObject.name, nm = f.FsmName ?? "";
                bool want = false;
                if (go == objectName.Value && HasAmmoVars(f.Fsm)) want = true;
                else if (go.IndexOf("ammo_box", StringComparison.OrdinalIgnoreCase) >= 0 && boxes < 4) { want = true; boxes++; }
                else if (nm == "Reload" && !reload) { want = true; reload = true; }
                if (!want || total >= 30) continue;
                total++;
                try { DescribeFsm(f.Fsm, go, sb); }
                catch (Exception e) { sb.AppendLine("  (describe failed: " + e.Message + ")"); }
            }
            string path = Path.Combine(Paths.BepInExRootPath, "ammo_logic.txt");
            File.WriteAllText(path, sb.ToString());
            Logger.LogInfo("Wrote " + path + " (" + total + " FSMs, " + sb.Length + " chars)");
        }

        static bool HasAmmoVars(Fsm fsm)
        {
            if (fsm.Variables == null) return false;
            foreach (var v in fsm.Variables.IntVariables) if (v != null && v.Name != null && IsAmmoName(v.Name)) return true;
            foreach (var v in fsm.Variables.FloatVariables) if (v != null && v.Name != null && IsAmmoName(v.Name)) return true;
            return false;
        }

        static object Member(object o, string name)
        {
            if (o == null) return null;
            var t = o.GetType();
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var p = t.GetProperty(name, flags);
            if (p != null && p.GetIndexParameters().Length == 0) return p.GetValue(o, null);
            var fi = t.GetField(name, flags);
            return fi != null ? fi.GetValue(o) : null;
        }

        static string Describe(object v, int depth)
        {
            if (v == null) return "null";
            if (v is string) return "\"" + v + "\"";
            if (v is NamedVariable)
            {
                var nv = (NamedVariable)v;
                object use = Member(nv, "UseVariable");
                if (use is bool && (bool)use && !string.IsNullOrEmpty(nv.Name)) return "$" + nv.Name;
                object raw = Member(nv, "RawValue");
                return raw == null ? "null" : raw.ToString();
            }
            var t = v.GetType();
            if (t.IsPrimitive || t.IsEnum) return v.ToString();
            if (t.Name == "FsmEvent") return "event:" + Member(v, "Name");
            if (t.Name == "FsmOwnerDefault") return "owner(" + Member(v, "OwnerOption") + ", " + Describe(Member(v, "GameObject"), depth + 1) + ")";
            if (v is IEnumerable && depth < 1)
            {
                var parts = new List<string>();
                foreach (var item in (IEnumerable)v) { parts.Add(Describe(item, depth + 1)); if (parts.Count >= 10) break; }
                return "[" + string.Join(", ", parts.ToArray()) + "]";
            }
            return t.Name;
        }

        static void DescribeFsm(Fsm fsm, string go, StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("=== " + go + " / FSM '" + fsm.Name + "'   active state: " + fsm.ActiveStateName);
            var states = Member(fsm, "States") as IEnumerable;
            if (states == null) { sb.AppendLine("  (no States member found)"); return; }
            foreach (var st in states)
            {
                sb.AppendLine("  STATE " + Member(st, "Name"));
                var acts = Member(st, "Actions") as IEnumerable;
                if (acts != null)
                    foreach (var a in acts)
                    {
                        if (a == null) continue;
                        var line = new StringBuilder("    ACTION " + a.GetType().Name);
                        foreach (var fi in a.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
                        {
                            string d;
                            try { d = Describe(fi.GetValue(a), 0); } catch { d = "?"; }
                            if (d == "null" || d == "" || d == "[]") continue;
                            line.Append("  " + fi.Name + "=" + d);
                        }
                        string text = line.ToString();
                        sb.AppendLine(text.Length > 500 ? text.Substring(0, 500) + "..." : text);
                    }
                var trs = Member(st, "Transitions") as IEnumerable;
                if (trs != null)
                    foreach (var t in trs) sb.AppendLine("    -> on " + Member(t, "EventName") + " goto " + Member(t, "ToState"));
            }
        }

        // ---------------- plumbing ----------------
        // Detail lines are only written with [Developer] VerboseLog on.
        void Say(string message)
        {
            if (verbose != null && verbose.Value) Logger.LogInfo(message);
        }

        void Run(Action a, string what)
        {
            try { a(); } catch (Exception e) { Fault(what, e); }
        }

        internal void Fault(string what, Exception e)
        {
            int n; faultCounts.TryGetValue(what, out n);
            faultCounts[what] = ++n;
            if (n <= 3) Logger.LogError("'" + what + "' failed (" + n + "): " + e);
        }

        bool KeyPressed(KeyCode k)
        {
            if (!legacyBroken)
            {
                try { if (Input.GetKeyDown(k)) return true; }
                catch (Exception e) { legacyBroken = true; Say("legacy Input not usable: " + e.Message); }
            }
            if (!newBroken)
            {
                try { if (NewKeyPressed(k)) return true; }
                catch (Exception e) { newBroken = true; Say("new Input System not usable: " + e.Message); }
            }
            return false;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        bool NewKeyPressed(KeyCode k)
        {
            Key key;
            if (!Enum.TryParse<Key>(k.ToString(), out key)) return false;
            var kb = Keyboard.current;
            return kb != null && kb[key].wasPressedThisFrame;
        }
    }

    // Harmony hooks. Must never throw into the game.
    public static class Hooks
    {
        public static void BeforeUpdate(PlayMakerFSM __instance)
        {
            try { Plugin.Instance.OnUpdate(__instance); }
            catch (Exception e) { if (Plugin.Instance != null) Plugin.Instance.Fault("hook", e); }
        }

        public static void AfterStart(PlayMakerFSM __instance)
        {
            try { Plugin.Instance.OnStarted(__instance); }
            catch (Exception e) { if (Plugin.Instance != null) Plugin.Instance.Fault("hook", e); }
        }
    }
}
