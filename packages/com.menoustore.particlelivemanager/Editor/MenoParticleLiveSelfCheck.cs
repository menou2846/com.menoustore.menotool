using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using nadena.dev.modular_avatar.core;

namespace Menou.ParticleLiveManager
{
    public static class MenoParticleLiveSelfCheck
    {
        static string Prefab => MenoParticleLiveManagerWindow.TemplatePath;
        static int count;
        static void Assert(bool value, string message) { if (!value) throw new Exception("SelfCheck: " + message); count++; }
        static void Refuses(Action action, string message)
        {
            try { action(); } catch (InvalidOperationException) { count++; return; }
            throw new Exception("SelfCheck accepted: " + message);
        }
        static string SceneSnapshot(Scene scene) => string.Join("\n", scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).SelectMany(t => t.GetComponents<Component>()).Where(c => c).OrderBy(c => c.GetInstanceID()).Select(c => c.GetInstanceID() + ":" + EditorJsonUtility.ToJson(c)));
        static void Invoke(object target, string method)
        {
            try { target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, null); }
            catch (TargetInvocationException e) { throw e.InnerException ?? e; }
        }
        static void NoExtrapolation(AnimationTrack track, int mode = 0)
        {
            var serialized = new SerializedObject(track);
            var clip = serialized.FindProperty("m_Clips").GetArrayElementAtIndex(0);
            clip.FindPropertyRelative("m_PreExtrapolationMode").enumValueIndex = mode;
            clip.FindPropertyRelative("m_PostExtrapolationMode").enumValueIndex = mode;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        public static string Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || AnimationMode.InAnimationMode()) throw new InvalidOperationException("プレビュー/Playを停止してください。");
            count = 0;
            var active = SceneManager.GetActiveScene();
            var snapshot = SceneSnapshot(active);
            var dirty = active.isDirty;
            var protectedPaths = AssetDatabase.GetDependencies(Prefab, true).Where(p => p.StartsWith("Assets/") && File.Exists(p)).Concat(new[] { active.path }).Where(File.Exists).Distinct().ToArray();
            var hashes = protectedPaths.ToDictionary(p => p, p => Convert.ToBase64String(System.Security.Cryptography.SHA256.Create().ComputeHash(File.ReadAllBytes(p))));
            var scene = EditorSceneManager.NewPreviewScene();
            var folder = "Assets/__MenoParticleLiveSelfCheck_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            MenoParticleLiveManagerWindow window = null;
            MenoParticleLiveManagerWindow freshWindow = null;
            var automaticAssets = new List<string>();
            Undo.IncrementCurrentGroup();
            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
                var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                var avatar = new GameObject("SelfCheckAvatar");
                SceneManager.MoveGameObjectToScene(avatar, scene);
                avatar.AddComponent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
                root.transform.SetParent(avatar.transform, false);
                var originalMA = root.GetComponent<ModularAvatarMergeAnimator>();
                var originalController = originalMA.animator;
                var originalMAJson = EditorJsonUtility.ToJson(originalMA);
                var maSettings = EditorJsonUtility.ToJson(root.transform.Find("MA設定").gameObject);
                var profile = ScriptableObject.CreateInstance<MenoParticleLiveProfile>();
                AssetDatabase.CreateAsset(profile, folder + "/Profile.asset");
                window = ScriptableObject.CreateInstance<MenoParticleLiveManagerWindow>();
                var flags = BindingFlags.NonPublic | BindingFlags.Instance;
                window.GetType().GetField("root", flags).SetValue(window, root);
                window.GetType().GetField("profile", flags).SetValue(window, profile);
                var sceneBeforeSelection = SceneSnapshot(scene);
                window.SelectAvatar(avatar);
                Assert((GameObject)window.GetType().GetField("root", flags).GetValue(window) == root, "avatar resolves existing live");
                Assert(SceneSnapshot(scene) == sceneBeforeSelection, "avatar selection is read only");
                Refuses(() => window.SelectAvatar(prefab), "asset instead of avatar");
                Refuses(() => window.SelectAvatar(root), "live instead of avatar");
                Invoke(window, "Setup");
                var director = (PlayableDirector)window.GetType().GetField("director", flags).GetValue(window);
                Assert(director && director.CompareTag("EditorOnly") && !director.playOnAwake, "authoring strip boundary");
                Assert(profile.timeline == director.playableAsset, "timeline association");
                var savedProfile = File.ReadAllText(AssetDatabase.GetAssetPath(profile));
                Assert(savedProfile.Contains(profile.rootId) && savedProfile.Contains(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(profile.timeline))), "association persisted to profile asset");
                Assert(profile.timeline.GetOutputTracks().OfType<AnimationTrack>().Count() == 3, "three starter performance tracks");
                Assert(profile.timeline.GetOutputTracks().OfType<AnimationTrack>().All(t => director.GetGenericBinding(t) is Animator), "automatic performance bindings");
                Assert(new[] { "particle", "音源", "skybox", "FX" }.All(n => root.transform.Find("ライブ/" + n)) && !root.transform.Find("ライブ/ぱーちくる"), "common hierarchy uses particle");
                Assert(profile.timeline.GetOutputTracks().OfType<AnimationTrack>().All(t => t.infiniteClip && AssetDatabase.GetAssetPath(t.infiniteClip) == AssetDatabase.GetAssetPath(profile.timeline)), "native recording clips saved in timeline");
                Assert(profile.authoringControllers.Length == 3 && profile.authoringControllers.All(c => c && MenoParticleLiveAuthoringPlugin.IsAuthoring(c)), "owned category controllers created");
                Assert(profile.timeline.GetOutputTracks().OfType<AnimationTrack>().All(t => ((Animator)director.GetGenericBinding(t)).runtimeAnimatorController.animationClips.Contains(t.infiniteClip)), "category controllers reference exact Recorded clips");
                var unbuiltClone = UnityEngine.Object.Instantiate(avatar);
                SceneManager.MoveGameObjectToScene(unbuiltClone, scene);
                MenoParticleLiveAuthoringPlugin.StripControllers(unbuiltClone);
                Assert(!unbuiltClone.GetComponentsInChildren<Animator>(true).Any(a => MenoParticleLiveAuthoringPlugin.IsAuthoring(a.runtimeAnimatorController)), "no runtime data still strips authoring references without exception");
                Assert(profile.timeline.GetOutputTracks().OfType<AnimationTrack>().All(t => ((Animator)director.GetGenericBinding(t)).runtimeAnimatorController), "stripping unbuilt clone preserves source controllers");
                UnityEngine.Object.DestroyImmediate(unbuiltClone);
                Assert(!root.transform.Find("ライブ/音源").GetComponent<Animator>(), "unused audio Animator removed");
                var audioAnimator = root.transform.Find("ライブ/音源").gameObject.AddComponent<Animator>();
                audioAnimator.runtimeAnimatorController = originalController;
                Invoke(window, "RemoveEmptyAudioAnimator");
                Assert(audioAnimator && audioAnimator.runtimeAnimatorController == originalController, "nonempty audio Animator preserved");
                audioAnimator.runtimeAnimatorController = null;
                director.SetGenericBinding(profile.timeline.GetOutputTracks().OfType<AnimationTrack>().First(), audioAnimator);
                Invoke(window, "RemoveEmptyAudioAnimator");
                Assert(audioAnimator, "bound audio Animator preserved");
                director.SetGenericBinding(profile.timeline.GetOutputTracks().OfType<AnimationTrack>().First(), root.transform.Find("ライブ/particle").GetComponent<Animator>());
                Invoke(window, "RemoveEmptyAudioAnimator");
                int transforms = root.GetComponentsInChildren<Transform>(true).Length;
                Invoke(window, "Setup");
                Assert(transforms == root.GetComponentsInChildren<Transform>(true).Length, "idempotent setup");
                var firstAuthoringControllers = profile.authoringControllers.ToArray();
                Invoke(window, "Setup");
                Assert(firstAuthoringControllers.SequenceEqual(profile.authoringControllers), "setup reuses owned authoring controllers");
                var particleAnimator = root.transform.Find("ライブ/particle").GetComponent<Animator>();
                particleAnimator.runtimeAnimatorController = originalController;
                Refuses(() => Invoke(window, "Setup"), "existing user controller preserved");
                Assert(particleAnimator.runtimeAnimatorController == originalController, "user controller not overwritten");
                particleAnimator.runtimeAnimatorController = profile.authoringControllers[0];
                var clip = new AnimationClip();
                var binding = EditorCurveBinding.FloatCurve("", typeof(GameObject), "m_IsActive");
                AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0, 1, 1));
                AssetDatabase.CreateAsset(clip, folder + "/Source.anim");
                var sourceJson = EditorJsonUtility.ToJson(clip);
                var track = profile.timeline.CreateTrack<AnimationTrack>(null, "FX");
                var cue = track.CreateClip(clip); cue.start = 0; cue.duration = 1;
                NoExtrapolation(track);
                director.SetGenericBinding(track, root.transform.Find("ライブ/FX").GetComponent<Animator>());
                var control = profile.timeline.CreateTrack<ControlTrack>(null, "Manual simulation");
                var controlCue = control.CreateClip<ControlPlayableAsset>(); controlCue.duration = 1;
                var controlJson = EditorJsonUtility.ToJson(control);
                Assert(MenoParticleLiveGenerator.Check(root, director, profile).Contains("生成可能"), "supported curve");
                NoExtrapolation(track, 1);
                Assert(MenoParticleLiveGenerator.Check(root, director, profile).Contains("生成可能"), "default hold is safe for full-length clip");
                NoExtrapolation(track);
                MenoParticleLiveGenerator.Generate(root, director, profile);
                var firstController = profile.generatedController;
                var firstPath = AssetDatabase.GetAssetPath(firstController);
                var firstLive = profile.generatedLive;
                var merge = MenoParticleLiveManagerWindow.Resolve<ModularAvatarMergeAnimator>(profile.mergeId);
                Assert(merge && merge.relativePathRoot.Get(merge) == root, "MA relative root");
                Assert(merge.animator == firstController && merge.mergeAnimatorMode == MergeAnimatorMode.Append, "MA append");
                Assert(profile.generatedLive && profile.generatedReset, "clips generated");
                Assert(!AssetDatabase.GetDependencies(AssetDatabase.GetAssetPath(firstController), true).Any(p => p.EndsWith(".playable") || p.EndsWith("Profile.asset")), "runtime FX has no authoring references");
                Assert(AnimationUtility.GetCurveBindings(profile.generatedLive).Single().path == "ライブ/FX", "rebased clip path");
                var states = firstController.layers[0].stateMachine.states.Select(s => s.state).ToArray();
                Assert(states.Length == 2 && states.All(s => !s.writeDefaultValues), "two states");
                Assert(states.All(s => s.transitions.Single().duration == 0 && !s.transitions.Single().hasExitTime), "immediate gate transitions");
                Assert(firstController.parameters.Single().name == "パーティクルライブSW", "reuse existing switch");
                var buildClone = UnityEngine.Object.Instantiate(avatar);
                SceneManager.MoveGameObjectToScene(buildClone, scene);
                MenoParticleLiveAuthoringPlugin.StripControllers(buildClone);
                Assert(!buildClone.GetComponentsInChildren<Animator>(true).Any(a => MenoParticleLiveAuthoringPlugin.IsAuthoring(a.runtimeAnimatorController)), "build clone has no authoring controller references");
                Assert(buildClone.GetComponentsInChildren<ModularAvatarMergeAnimator>(true).Any(m => m.animator == firstController), "build clone keeps runtime MA FX");
                Assert(root.transform.Find("ライブ/particle").GetComponent<Animator>().runtimeAnimatorController == profile.authoringControllers[0], "build stripping does not change authoring scene");
                UnityEngine.Object.DestroyImmediate(buildClone);
                MenoParticleLiveGenerator.Generate(root, director, profile);
                Assert(firstController != profile.generatedController && AssetDatabase.LoadAssetAtPath<AnimatorController>(firstPath) == firstController, "versioned update keeps old asset");
                Assert(root.GetComponentsInChildren<ModularAvatarMergeAnimator>(true).Length == 2, "update reuses generated MA");
                Assert(firstLive && AnimationUtility.GetCurveBindings(firstLive).Length == 1, "old clip retained");
                cue.start = 0.5; Refuses(() => MenoParticleLiveGenerator.Generate(root, director, profile), "offset start"); cue.start = 0;
                cue.timeScale = 2; Refuses(() => MenoParticleLiveGenerator.Check(root, director, profile), "retiming"); cue.timeScale = 1;
                cue.easeInDuration = 0.1; Refuses(() => MenoParticleLiveGenerator.Check(root, director, profile), "blend"); cue.easeInDuration = 0;
                director.gameObject.tag = "Untagged"; Refuses(() => MenoParticleLiveGenerator.Check(root, director, profile), "runtime director"); director.gameObject.tag = "EditorOnly";
                director.SetGenericBinding(track, root.transform.Find("ライブ").GetComponent<Animator>());
                Refuses(() => MenoParticleLiveGenerator.Check(root, director, profile), "protected live gate");
                director.SetGenericBinding(track, root.transform.Find("ライブ/FX").GetComponent<Animator>());
                merge.animator = originalController; Refuses(() => MenoParticleLiveGenerator.Generate(root, director, profile), "manual merge edit"); merge.animator = profile.generatedController;
                track.muted = true; Refuses(() => MenoParticleLiveGenerator.Check(root, director, profile), "control-only timeline"); track.muted = false;
                var duplicate = profile.timeline.CreateTrack<AnimationTrack>(null, "Duplicate");
                var dc = duplicate.CreateClip(clip); dc.duration = 1; NoExtrapolation(duplicate);
                director.SetGenericBinding(duplicate, root.transform.Find("ライブ/FX").GetComponent<Animator>());
                Refuses(() => MenoParticleLiveGenerator.Check(root, director, profile), "duplicate curve"); duplicate.muted = true;
                var wav = folder + "/Silence.wav";
                using (var writer = new BinaryWriter(File.Create(wav)))
                {
                    writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(88236); writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
                    writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(44100); writer.Write(88200); writer.Write((short)2); writer.Write((short)16);
                    writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(88200); writer.Write(new byte[88200]);
                }
                AssetDatabase.ImportAsset(wav, ImportAssetOptions.ForceSynchronousImport);
                profile.audio = AssetDatabase.LoadAssetAtPath<AudioClip>(wav);
                Invoke(window, "ApplyAudio");
                var audio = MenoParticleLiveManagerWindow.Resolve<AudioSource>(profile.audioId);
                Assert(audio && audio.clip == profile.audio && audio.playOnAwake && !audio.loop && audio.gameObject.activeSelf, "runtime audio defaults on");
                Assert(profile.previewTrack && director.GetGenericBinding(profile.previewTrack) != audio, "separate preview source");
                var preview = director.GetGenericBinding(profile.previewTrack) as AudioSource;
                Assert(preview && preview.transform.IsChildOf(director.transform), "preview stays under EditorOnly");
                Invoke(window, "ApplyAudio");
                Assert(profile.timeline.GetOutputTracks().OfType<AudioTrack>().Count() == 1, "audio update reuses owned track");
                MenoParticleLiveGenerator.Generate(root, director, profile);
                Assert(!AnimationUtility.GetCurveBindings(profile.generatedLive).Concat(AnimationUtility.GetCurveBindings(profile.generatedReset)).Any(b => b.path.Contains("RuntimeAudio")), "no generated runtime audio gate");
                track.muted = true;
                MenoParticleLiveGenerator.Generate(root, director, profile);
                Assert(AnimationUtility.GetCurveBindings(profile.generatedLive).Length == 0 && AnimationUtility.GetCurveBindings(profile.generatedReset).Length == 0, "audio-only export relies on canonical live gate");
                track.muted = false;
                var recordedTrack = profile.timeline.GetOutputTracks().OfType<AnimationTrack>().Single(t => t.name == "particle");
                var recorded = recordedTrack.infiniteClip;
                var recordedBinding = EditorCurveBinding.FloatCurve("sample_Particle", typeof(GameObject), "m_IsActive");
                AnimationUtility.SetEditorCurve(recorded, recordedBinding, AnimationCurve.Constant(0, 0.75f, 1));
                var recordedJson = EditorJsonUtility.ToJson(recorded);
                Invoke(window, "Setup");
                Assert(recordedTrack.infiniteClip == recorded && EditorJsonUtility.ToJson(recorded) == recordedJson, "setup preserves existing recorded animation");
                MenoParticleLiveGenerator.Generate(root, director, profile);
                Assert(AnimationUtility.GetCurveBindings(profile.generatedLive).Any(b => b.path == "ライブ/particle/sample_Particle"), "recorded curve rebased to particle");
                Assert(EditorJsonUtility.ToJson(recorded) == recordedJson, "export does not rewrite recorded clip");
                Assert(!AssetDatabase.GetDependencies(AssetDatabase.GetAssetPath(profile.generatedController), true).Any(p => p.EndsWith(".playable")), "recorded export has no timeline dependency");
                recordedTrack.infiniteClipOffsetPosition = Vector3.one;
                Refuses(() => MenoParticleLiveGenerator.Check(root, director, profile), "recorded offset rejected");
                recordedTrack.infiniteClipOffsetPosition = Vector3.zero;
                Assert(EditorJsonUtility.ToJson(control) == controlJson && profile.timeline.GetOutputTracks().Contains(control), "control track unchanged");
                Assert(EditorJsonUtility.ToJson(clip) == sourceJson, "source animation unchanged");
                Assert(EditorJsonUtility.ToJson(originalMA) == originalMAJson && originalMA.animator == originalController, "canonical MA unchanged");
                Assert(EditorJsonUtility.ToJson(root.transform.Find("MA設定").gameObject) == maSettings, "MA settings unchanged");
                Assert(root.GetComponentInChildren<PlayableDirector>(true).gameObject.CompareTag("EditorOnly"), "all generated directors stripped");
                var freshAvatar = new GameObject("FreshAvatar");
                SceneManager.MoveGameObjectToScene(freshAvatar, scene);
                freshAvatar.AddComponent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
                var freshProfile = ScriptableObject.CreateInstance<MenoParticleLiveProfile>();
                freshProfile.audio = profile.audio;
                AssetDatabase.CreateAsset(freshProfile, folder + "/FreshProfile.asset");
                freshWindow = ScriptableObject.CreateInstance<MenoParticleLiveManagerWindow>();
                freshWindow.GetType().GetField("profile", flags).SetValue(freshWindow, freshProfile);
                freshWindow.SelectAvatar(freshAvatar);
                Assert(MenoParticleLiveManagerWindow.FindLives(freshAvatar).Length == 0, "fresh avatar before prepare");
                Invoke(freshWindow, "Prepare");
                var freshRoot = MenoParticleLiveManagerWindow.FindLives(freshAvatar).Single();
                var freshDirector = freshRoot.GetComponentInChildren<PlayableDirector>(true);
                Assert(freshRoot.transform.parent == freshAvatar.transform && PrefabUtility.GetCorrespondingObjectFromSource(freshRoot) == prefab, "one-click template placement preserves source");
                Assert(freshDirector && freshDirector.CompareTag("EditorOnly"), "one-click authoring director");
                Assert(freshProfile.previewTrack && freshDirector.GetGenericBinding(freshProfile.previewTrack) is AudioSource, "one-click preview audio");
                Assert(freshProfile.timeline.GetOutputTracks().OfType<AnimationTrack>().All(t => freshDirector.GetGenericBinding(t) is Animator), "one-click performance bindings");
                var freshCount = freshRoot.GetComponentsInChildren<Transform>(true).Length;
                Invoke(freshWindow, "Prepare");
                Assert(MenoParticleLiveManagerWindow.FindLives(freshAvatar).Length == 1 && freshRoot.GetComponentsInChildren<Transform>(true).Length == freshCount, "prepare twice does not duplicate hierarchy");
                MenoParticleLiveGenerator.Generate(freshRoot, freshDirector, freshProfile);
                Assert(freshProfile.generatedController && AnimationUtility.GetCurveBindings(freshProfile.generatedLive).Length == 0, "fresh avatar audio-only export");
                var previousProfileJson = EditorJsonUtility.ToJson(freshProfile);
                var previousTimeline = freshProfile.timeline;
                UnityEngine.Object.DestroyImmediate(freshRoot);
                Assert(MenoParticleLiveManagerWindow.FindLives(freshAvatar).Length == 0, "live removed before recreation");
                Invoke(freshWindow, "Prepare");
                var recreatedProfile = (MenoParticleLiveProfile)freshWindow.GetType().GetField("profile", flags).GetValue(freshWindow);
                automaticAssets.Add(AssetDatabase.GetAssetPath(recreatedProfile));
                automaticAssets.Add(Path.GetDirectoryName(AssetDatabase.GetAssetPath(recreatedProfile.timeline)).Replace('\\', '/'));
                Assert(automaticAssets.All(p => p.StartsWith("Assets/MenoTools/ParticleLiveManagerV1/Generated/", StringComparison.Ordinal)), "recreated assets use dedicated new paths");
                Assert(recreatedProfile != freshProfile && recreatedProfile.timeline != previousTimeline, "removed live gets a new profile and timeline");
                Assert(recreatedProfile.liveName == freshProfile.liveName && recreatedProfile.prefix == freshProfile.prefix && recreatedProfile.audio == freshProfile.audio, "recreation keeps input values");
                Assert(EditorJsonUtility.ToJson(freshProfile) == previousProfileJson && previousTimeline, "previous profile and timeline preserved");
                freshRoot = MenoParticleLiveManagerWindow.FindLives(freshAvatar).Single();
                Assert(freshRoot.GetComponentInChildren<PlayableDirector>(true) && recreatedProfile.timeline.GetOutputTracks().OfType<AnimationTrack>().All(t => t.infiniteClip), "empty avatar recreation completes recording setup");
                freshWindow.GetType().GetField("profile", flags).SetValue(freshWindow, freshProfile);
                UnityEngine.Object.DestroyImmediate(freshRoot);
                var selectionBefore = SceneSnapshot(scene);
                freshWindow.SelectAvatar(freshAvatar);
                Assert(freshWindow.GetType().GetField("profile", flags).GetValue(freshWindow) == null && freshWindow.GetType().GetField("director", flags).GetValue(freshWindow) == null, "empty avatar selection detaches stale ownership");
                Assert(SceneSnapshot(scene) == selectionBefore && EditorJsonUtility.ToJson(freshProfile) == previousProfileJson, "detaching previous live is read only");
                freshWindow.GetType().GetField("profile", flags).SetValue(freshWindow, recreatedProfile);
                freshRoot = (GameObject)PrefabUtility.InstantiatePrefab(prefab, freshAvatar.transform);
                var secondLive = (GameObject)PrefabUtility.InstantiatePrefab(prefab, freshAvatar.transform);
                secondLive.name = "SecondLive";
                freshWindow.GetType().GetField("root", flags).SetValue(freshWindow, null);
                var multipleBefore = SceneSnapshot(scene);
                Refuses(() => Invoke(freshWindow, "Prepare"), "multiple live selection required");
                Assert(SceneSnapshot(scene) == multipleBefore, "ambiguous selection changes nothing");
                freshWindow.GetType().GetField("root", flags).SetValue(freshWindow, secondLive);
                Refuses(() => Invoke(freshWindow, "Prepare"), "cross-live profile cannot be reused");
                foreach (var pair in hashes) Assert(pair.Value == Convert.ToBase64String(System.Security.Cryptography.SHA256.Create().ComputeHash(File.ReadAllBytes(pair.Key))), "protected disk hash: " + pair.Key);
            }
            finally
            {
                if (window) UnityEngine.Object.DestroyImmediate(window);
                if (freshWindow) UnityEngine.Object.DestroyImmediate(freshWindow);
                // Clear only test objects; a global Undo replay also invokes unrelated SDK inspectors.
                foreach (var transform in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)))
                {
                    foreach (var component in transform.GetComponents<Component>().Where(c => c)) Undo.ClearUndo(component);
                    Undo.ClearUndo(transform.gameObject);
                }
                foreach (var asset in AssetDatabase.FindAssets("", new[] { folder }).Select(AssetDatabase.GUIDToAssetPath).Distinct().SelectMany(AssetDatabase.LoadAllAssetsAtPath).Where(a => a)) Undo.ClearUndo(asset);
                foreach (var path in automaticAssets)
                    foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path)) Undo.ClearUndo(asset);
                EditorSceneManager.ClosePreviewScene(scene);
                AssetDatabase.DeleteAsset(folder);
                foreach (var path in automaticAssets) AssetDatabase.DeleteAsset(path);
                Undo.IncrementCurrentGroup();
            }
            Assert(SceneManager.GetActiveScene() == active && active.isDirty == dirty && SceneSnapshot(active) == snapshot, "user scene unchanged including dirty state");
            return "PARTICLE_LIVE_MANAGER_V1_SELF_CHECK_OK: " + count + " checks. No Play / Scene save.";
        }
    }
}
