using UnityEngine;
using UnityEngine.Timeline;
using UnityEditor.Animations;

namespace Menou.ParticleLiveManager
{
    public sealed class MenoParticleLiveProfile : ScriptableObject
    {
        public string liveName = "New Live", prefix = "MenoLive";
        public AudioClip audio;
        public TimelineAsset timeline;
        public AudioTrack previewTrack;
        public string previewSourceId;
        public AnimatorController generatedController;
        public AnimatorController[] authoringControllers = new AnimatorController[3];
        public AnimationClip generatedLive, generatedReset;
        public string rootId, directorId, audioId, mergeId;
    }
}
