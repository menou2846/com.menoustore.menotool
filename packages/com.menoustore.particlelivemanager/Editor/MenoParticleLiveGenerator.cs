using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using nadena.dev.modular_avatar.core;
using VRC.SDK3.Avatars.Components;

namespace Menou.ParticleLiveManager
{
    public static class MenoParticleLiveGenerator
    {
        const string Switch = "パーティクルライブSW";
        internal static void ValidateRoot(GameObject root)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || AnimationMode.InAnimationMode())
                throw new InvalidOperationException("Play/Compile/プレビュー中は実行できません。");
            if (!root || EditorUtility.IsPersistent(root) || !root.scene.IsValid() || PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("通常Scene上のPrefabルートを指定してください。");
            if (root.transform.Cast<Transform>().Count(t => t.name == "ライブ") != 1 || root.transform.Cast<Transform>().Count(t => t.name == "MA設定") != 1)
                throw new InvalidOperationException("直下の『ライブ』『MA設定』が一意である必要があります。既存構造は自動変更しません。");
            var canonical = root.GetComponent<ModularAvatarMergeAnimator>();
            if (!canonical || canonical.pathMode != MergeAnimatorPathMode.Relative || canonical.mergeAnimatorMode != MergeAnimatorMode.Append ||
                canonical.layerType != VRCAvatarDescriptor.AnimLayerType.FX || canonical.relativePathRoot.Get(canonical) != null && canonical.relativePathRoot.Get(canonical) != root)
                throw new InvalidOperationException("参照Prefabと異なるMA構成です。Relative/Append/FXの既存ルートMAを確認してください。");
            var controller = canonical.animator as AnimatorController;
            if (!controller || !controller.parameters.Any(p => p.name == Switch && p.type == AnimatorControllerParameterType.Bool))
                throw new InvalidOperationException("既存Boolパラメータ『" + Switch + "』が見つかりません。");
            foreach (var parent in root.GetComponentsInChildren<Transform>(true))
                if (parent.Cast<Transform>().Any(t => t.name.Contains('/')) || parent.Cast<Transform>().GroupBy(t => t.name).Any(g => g.Count() > 1))
                    throw new InvalidOperationException("曖昧なHierarchyパスがあります: " + parent.name);
        }
        public static string Audit(GameObject root)
        {
            ValidateRoot(root);
            var canonical = root.GetComponent<ModularAvatarMergeAnimator>();
            return "MA: " + AssetDatabase.GetAssetPath(canonical.animator) + "\nライブON/OFF: " + Switch +
                "\n子階層: " + string.Join(", ", root.transform.Find("ライブ").Cast<Transform>().Select(t => t.name)) +
                "\nDirector: " + root.GetComponentsInChildren<PlayableDirector>(true).Length +
                " / AudioSource: " + root.GetComponentsInChildren<AudioSource>(true).Length + "\n読み取りのみ。固有演出は共通仕様に採用しません。";
        }
        internal static string OutputFolder(MenoParticleLiveProfile profile)
        {
            var path = AssetDatabase.GetAssetPath(profile);
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal)) throw new InvalidOperationException("制作設定はAssets配下へ保存してください。");
            var name = profile.prefix;
            if (string.IsNullOrWhiteSpace(name) || name.Any(c => Path.GetInvalidFileNameChars().Contains(c) || c == '/' || c == '\\') || name == "." || name == "..")
                throw new InvalidOperationException("接頭辞に使えない文字があります。");
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            var folder = AssetDatabase.GenerateUniqueAssetPath(parent + "/" + name + "_Generated");
            if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, Path.GetFileName(folder)))) throw new InvalidOperationException("出力フォルダを作成できません。");
            return folder;
        }
        static IEnumerable<TrackAsset> AllTracks(TrackAsset track)
        {
            yield return track;
            foreach (var child in track.GetChildTracks()) foreach (var item in AllTracks(child)) yield return item;
        }
        sealed class Binding
        {
            public EditorCurveBinding binding;
            public AnimationCurve curve;
            public ObjectReferenceKeyframe[] objects;
        }
        static string Key(EditorCurveBinding b) => b.path + "|" + b.type.FullName + "|" + b.propertyName;
        static List<Binding> Collect(GameObject root, PlayableDirector director, MenoParticleLiveProfile profile, out string notes)
        {
            ValidateRoot(root);
            if (!profile || !string.IsNullOrEmpty(profile.rootId) && MenoParticleLiveManagerWindow.Resolve<GameObject>(profile.rootId) != root)
                throw new InvalidOperationException("制作設定の対象ルートが一致しません。");
            if (!root.GetComponentInParent<VRCAvatarDescriptor>()) throw new InvalidOperationException("生成前にPrefabルートを対象アバターの子へ配置してください。MAのパス基準を安全に確定できません。");
            if (!director || !(director.playableAsset is TimelineAsset) || !director.CompareTag("EditorOnly") ||
                director.gameObject.scene != root.scene || !director.transform.IsChildOf(root.transform) ||
                director.transform == root.transform || director.transform.IsChildOf(root.transform.Find("ライブ")) ||
                director.transform.IsChildOf(root.transform.Find("MA設定")) || director.state == PlayState.Playing)
                throw new InvalidOperationException("制作専用EditorOnly Directorが必要です。");
            var timeline = (TimelineAsset)director.playableAsset;
            if (profile.timeline != timeline) throw new InvalidOperationException("制作設定とDirectorのTimelineが一致しません。");
            var tracks = timeline.GetRootTracks().SelectMany(AllTracks).ToArray();
            if (timeline.markerTrack && timeline.markerTrack.GetMarkers().Any() || tracks.Any(t => t.GetMarkers().Any()))
                throw new InvalidOperationException("Marker/SignalはV1変換対象外です。別Timelineへ分離してください。");
            var result = new List<Binding>();
            var keys = new HashSet<string>();
            var duration = timeline.duration;
            if (double.IsNaN(duration) || double.IsInfinity(duration) || duration <= 0 || duration > 3600) throw new InvalidOperationException("Timeline長は0秒超〜3600秒にしてください。");
            foreach (var track in tracks.OfType<AnimationTrack>().Where(t => !t.mutedInHierarchy))
            {
                var clips = track.GetClips().ToArray();
                if (clips.Length == 0 && !track.infiniteClip) continue;
                var animator = director.GetGenericBinding(track) as Animator;
                var clip = clips.Length == 1 ? clips[0] : null;
                var asset = clip != null ? clip.asset as AnimationPlayableAsset : null;
                bool infinite = clips.Length == 0 && track.infiniteClip;
                var source = infinite ? track.infiniteClip : asset ? asset.clip : null;
                if (source && AnimationUtility.GetCurveBindings(source).Length == 0 && AnimationUtility.GetObjectReferenceCurveBindings(source).Length == 0 && AnimationUtility.GetAnimationEvents(source).Length == 0) continue;
                // shortcut: only direct, unblended property animation, add a Timeline evaluator before supporting mixing or retiming.
                if (!animator || !animator.transform.IsChildOf(root.transform.Find("ライブ")) || track.parent is AnimationTrack || track.GetChildTracks().Any() ||
                    track.avatarMask || track.position != Vector3.zero || track.rotation != Quaternion.identity || track.trackOffset != TrackOffset.ApplyTransformOffsets ||
                    !source || source.humanMotion || source.legacy || source.isLooping || AnimationUtility.GetAnimationEvents(source).Length != 0)
                    throw new InvalidOperationException("演出「" + track.name + "」の録画・関連付けを確認してください。重ね合わせ・ループ・オフセットなどはV1未対応です。");
                if (infinite)
                {
                    var serialized = new SerializedObject(track);
                    var offset = serialized.FindProperty("m_InfiniteClipTimeOffset");
                    var loop = serialized.FindProperty("mInfiniteClipLoop");
                    if (offset == null || loop == null || Math.Abs(offset.doubleValue) > 0.00001 || loop.intValue == (int)AnimationPlayableAsset.LoopMode.On ||
                        track.infiniteClipOffsetPosition != Vector3.zero || track.infiniteClipOffsetRotation != Quaternion.identity || source.length > duration + 0.001)
                        throw new InvalidOperationException("録画「" + track.name + "」の時間移動・ループ・オフセットはV1未対応です。");
                }
                else if (clip == null || asset == null ||
                    Math.Abs(clip.start) > 0.00001 || Math.Abs(clip.clipIn) > 0.00001 || Math.Abs(clip.timeScale - 1) > 0.00001 ||
                    Math.Abs(clip.duration - source.length) > 0.001 || Math.Abs(clip.duration - duration) > 0.001 ||
                    clip.mixInDuration > 0 || clip.mixOutDuration > 0 ||
                    clip.preExtrapolationMode != TimelineClip.ClipExtrapolation.None && clip.preExtrapolationMode != TimelineClip.ClipExtrapolation.Hold ||
                    clip.postExtrapolationMode != TimelineClip.ClipExtrapolation.None && clip.postExtrapolationMode != TimelineClip.ClipExtrapolation.Hold ||
                    asset.position != Vector3.zero || asset.rotation != Quaternion.identity || asset.loop == AnimationPlayableAsset.LoopMode.On)
                    throw new InvalidOperationException("演出「" + track.name + "」は書き出せません。0秒から、音源と同じ長さのアニメーション1本を置いてください。速度変更・重ね合わせ・ループなどはV1未対応です。");
                var prefix = AnimationUtility.CalculateTransformPath(animator.transform, root.transform);
                foreach (var binding in AnimationUtility.GetCurveBindings(source))
                {
                    var target = Rebase(root, prefix, binding);
                    if (!keys.Add(Key(target))) throw new InvalidOperationException("重複カーブ: " + target.path + "/" + target.propertyName);
                    var curve = AnimationUtility.GetEditorCurve(source, binding);
                    if (curve.length == 0 || curve.keys.Any(k => float.IsNaN(k.value) || float.IsInfinity(k.value))) throw new InvalidOperationException("無効なカーブです。");
                    result.Add(new Binding { binding = target, curve = curve });
                }
                foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(source))
                {
                    var target = Rebase(root, prefix, binding);
                    if (!keys.Add(Key(target))) throw new InvalidOperationException("重複カーブ: " + target.path);
                    var objects = AnimationUtility.GetObjectReferenceCurve(source, binding);
                    if (objects.Length == 0 || objects[0].time > 0 || objects.Any(k => k.value && !EditorUtility.IsPersistent(k.value))) throw new InvalidOperationException("Objectカーブは0秒のキーとAsset参照が必要です。");
                    result.Add(new Binding { binding = target, objects = objects });
                }
            }
            notes = "Control Track " + tracks.OfType<ControlTrack>().Count() + "本は変換せず保持。Audio Trackは制作プレビュー専用。";
            if (tracks.Any(t => !(t is AnimationTrack) && !(t is AudioTrack) && !(t is ControlTrack) && !(t is GroupTrack) && !t.mutedInHierarchy))
                throw new InvalidOperationException("未対応Trackがあります。Activation/Signal/カスタムTrackはV1生成対象外です。別Timelineへ分離してください。");
            var runtime = MenoParticleLiveManagerWindow.Resolve<AudioSource>(profile.audioId);
            if (runtime && !profile.audio) throw new InvalidOperationException("実行音源があるためAudioClipを指定してください。");
            if (profile.audio && (!runtime || runtime.clip != profile.audio || !runtime.playOnAwake || runtime.loop || !runtime.transform.IsChildOf(root.transform.Find("ライブ/音源")) || !runtime.gameObject.activeSelf))
                throw new InvalidOperationException("専用実行音源を反映し、デフォルトON・Play On Awake ON・Loop OFFを確認してください。");
            if (profile.audio && Math.Abs(profile.audio.length - duration) > 0.02) throw new InvalidOperationException("V1では音源長とTimeline全長を合わせてください。");
            if (runtime && result.Any(b => b.binding.path == AnimationUtility.CalculateTransformPath(runtime.transform, root.transform)))
                throw new InvalidOperationException("実行音源のON/OFFは「ライブ」に任せてください。音源専用のカーブは書き出しません。");
            if (result.Count == 0 && !profile.audio) throw new InvalidOperationException("音源も書き出せる演出もありません。音源を指定するか、Timelineで演出を録画してください。Control Trackは手動プレビュー専用です。");
            var existing = MenoParticleLiveManagerWindow.Resolve<ModularAvatarMergeAnimator>(profile.mergeId);
            if (!existing && !string.IsNullOrEmpty(profile.mergeId)) throw new InvalidOperationException("生成MA参照を解決できません。Sceneを確認してください。");
            if (existing && (existing.transform.parent != root.transform || existing.animator != profile.generatedController || existing.relativePathRoot.Get(existing) != root || existing.pathMode != MergeAnimatorPathMode.Relative || existing.layerType != VRCAvatarDescriptor.AnimLayerType.FX || existing.mergeAnimatorMode != MergeAnimatorMode.Append))
                throw new InvalidOperationException("生成MAが手動変更されています。自動更新を中止しました。");
            var protectedKeys = new HashSet<string>();
            foreach (var other in root.GetComponentsInChildren<ModularAvatarMergeAnimator>(true).Where(m => m != existing && m.animator))
            {
                if (other.pathMode != MergeAnimatorPathMode.Relative) throw new InvalidOperationException("Absoluteな既存MAとの競合はV1では判定できません。");
                var baseObject = other.relativePathRoot.Get(other) ?? other.gameObject;
                if (!baseObject.transform.IsChildOf(root.transform)) throw new InvalidOperationException("ルート外を基準とする既存MAがあります。");
                var basePath = AnimationUtility.CalculateTransformPath(baseObject.transform, root.transform);
                foreach (var b in other.animator.animationClips.SelectMany(c => AnimationUtility.GetCurveBindings(c).Concat(AnimationUtility.GetObjectReferenceCurveBindings(c))))
                {
                    var rebased = b;
                    rebased.path = string.IsNullOrEmpty(basePath) ? b.path : string.IsNullOrEmpty(b.path) ? basePath : basePath + "/" + b.path;
                    protectedKeys.Add(Key(rebased));
                }
            }
            if (result.Any(b => protectedKeys.Contains(Key(b.binding)))) throw new InvalidOperationException("既存MAのカーブと競合します。生成を中止しました。");
            return result;
        }
        static EditorCurveBinding Rebase(GameObject root, string prefix, EditorCurveBinding binding)
        {
            if (binding.path.Split('/').Any(s => s == ".." || s == ".")) throw new InvalidOperationException("無効なカーブパス。");
            binding.path = string.IsNullOrEmpty(binding.path) ? prefix : prefix + "/" + binding.path;
            if (!new[] { "ライブ/particle/", "ライブ/ぱーちくる/", "ライブ/音源/", "ライブ/skybox/", "ライブ/FX/" }.Any(p => (binding.path + "/").StartsWith(p, StringComparison.Ordinal)))
                throw new InvalidOperationException("保護対象へのカーブ: " + binding.path);
            var target = root.transform.Find(binding.path);
            if (!target || target.GetComponentsInParent<Transform>(true).Any(t => t.CompareTag("EditorOnly")) ||
                binding.type != typeof(GameObject) && !target.GetComponent(binding.type) ||
                binding.type == typeof(Animator) || binding.type == typeof(AudioSource) || binding.type == typeof(PlayableDirector) ||
                binding.type.Name.Contains("Constraint") || binding.type.Namespace != null && binding.type.Namespace.StartsWith("nadena."))
                throw new InvalidOperationException("変換できない参照: " + binding.path + " / " + binding.type.Name);
            if (AnimationUtility.GetAnimatedObject(root, binding) == null) throw new InvalidOperationException("解決できないカーブ: " + binding.path);
            if (!AnimationUtility.GetAnimatableBindings(target.gameObject, root).Any(b => b.path == binding.path && b.type == binding.type && b.propertyName == binding.propertyName))
                throw new InvalidOperationException("アニメーション可能でないプロパティ: " + binding.propertyName);
            if (binding.type == typeof(Transform)) throw new InvalidOperationException("V1ではTimelineのTransformオフセットを再現できないためTransformカーブは対象外です。");
            return binding;
        }
        public static string Check(GameObject root, PlayableDirector director, MenoParticleLiveProfile profile)
        {
            var bindings = Collect(root, director, profile, out var notes);
            return "生成可能: " + bindings.Count + "カーブ\n" + notes;
        }
        public static string Generate(GameObject root, PlayableDirector director, MenoParticleLiveProfile profile)
        {
            var bindings = Collect(root, director, profile, out var notes);
            var folder = OutputFolder(profile);
            var live = new AnimationClip { name = profile.prefix + "_Live", frameRate = 60 };
            var reset = new AnimationClip { name = profile.prefix + "_Reset", frameRate = 60 };
            foreach (var b in bindings)
            {
                if (b.objects != null)
                {
                    AnimationUtility.SetObjectReferenceCurve(live, b.binding, b.objects);
                    AnimationUtility.SetObjectReferenceCurve(reset, b.binding, new[] { new ObjectReferenceKeyframe { time = 0, value = b.objects[0].value } });
                }
                else
                {
                    AnimationUtility.SetEditorCurve(live, b.binding, b.curve);
                    AnimationUtility.SetEditorCurve(reset, b.binding, AnimationCurve.Constant(0, 1f / 60, b.curve.Evaluate(0)));
                }
            }
            AssetDatabase.CreateAsset(live, folder + "/Live.anim");
            AssetDatabase.CreateAsset(reset, folder + "/Reset.anim");
            var controller = AnimatorController.CreateAnimatorControllerAtPath(folder + "/FX.controller");
            controller.AddParameter(Switch, AnimatorControllerParameterType.Bool);
            var layers = controller.layers; layers[0].name = profile.prefix + "_Performance"; controller.layers = layers;
            var sm = controller.layers[0].stateMachine;
            var off = sm.AddState("OFF"); off.motion = reset; off.writeDefaultValues = false;
            var on = sm.AddState("ON"); on.motion = live; on.writeDefaultValues = false;
            sm.defaultState = off;
            var start = off.AddTransition(on); start.hasExitTime = false; start.duration = 0; start.AddCondition(AnimatorConditionMode.If, 0, Switch);
            var stop = on.AddTransition(off); stop.hasExitTime = false; stop.duration = 0; stop.AddCondition(AnimatorConditionMode.IfNot, 0, Switch);
            EditorUtility.SetDirty(controller); EditorUtility.SetDirty(sm); EditorUtility.SetDirty(off); EditorUtility.SetDirty(on);
            AssetDatabase.SaveAssetIfDirty(controller); AssetDatabase.SaveAssetIfDirty(live); AssetDatabase.SaveAssetIfDirty(reset);
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(folder + "/FX.controller") != controller || AnimationUtility.GetCurveBindings(live).Length + AnimationUtility.GetObjectReferenceCurveBindings(live).Length != bindings.Count)
                throw new InvalidOperationException("生成アセットの検証に失敗しました。既存参照は切り替えていません。");
            var merge = MenoParticleLiveManagerWindow.Resolve<ModularAvatarMergeAnimator>(profile.mergeId);
            if (!merge)
            {
                var go = new GameObject(GameObjectUtility.GetUniqueNameForSibling(root.transform, profile.prefix + "_GeneratedFX")); Undo.RegisterCreatedObjectUndo(go, "Create generated FX");
                Undo.SetTransformParent(go.transform, root.transform, "Create generated FX");
                merge = Undo.AddComponent<ModularAvatarMergeAnimator>(go);
            }
            Undo.RecordObject(merge, "Update generated FX");
            merge.animator = controller; merge.layerType = VRCAvatarDescriptor.AnimLayerType.FX;
            merge.pathMode = MergeAnimatorPathMode.Relative; merge.relativePathRoot.Set(root);
            merge.mergeAnimatorMode = MergeAnimatorMode.Append; merge.matchAvatarWriteDefaults = true; merge.deleteAttachedAnimator = false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(merge);
            Undo.RecordObject(profile, "Update generated references");
            profile.generatedController = controller; profile.generatedLive = live; profile.generatedReset = reset;
            profile.mergeId = MenoParticleLiveManagerWindow.Id(merge);
            EditorUtility.SetDirty(profile); AssetDatabase.SaveAssetIfDirty(profile);
            EditorSceneManager.MarkSceneDirty(root.scene);
            return "生成完了: " + folder + "\n" + notes + "\n旧出力は保持。Sceneは未保存。VRChat実機は未検証。";
        }
    }
}
