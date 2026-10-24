using System.Linq;
using UnityEditor;
using UnityEngine;
using nadena.dev.ndmf;

[assembly: ExportsPlugin(typeof(Menou.ParticleLiveManager.MenoParticleLiveAuthoringPlugin))]

namespace Menou.ParticleLiveManager
{
    public sealed class MenoParticleLiveAuthoringPlugin : Plugin<MenoParticleLiveAuthoringPlugin>
    {
        internal const string Label = "MenoParticleLiveAuthoring";
        internal const string OwnerPrefix = "MenoParticleLiveOwner_";
        public override string QualifiedName => "menou.particle-live-manager.authoring";
        public override string DisplayName => "Meno Particle Live Manager";
        internal static bool IsAuthoring(UnityEngine.Object controller) => controller && AssetDatabase.GetLabels(controller).Contains(Label);
        internal static void StripControllers(GameObject avatar)
        {
            var targets = avatar.GetComponentsInChildren<Animator>(true).Where(a => IsAuthoring(a.runtimeAnimatorController)).ToArray();
            foreach (var animator in targets) animator.runtimeAnimatorController = null;
        }
        protected override void Configure()
        {
            InPhase(BuildPhase.Resolving).BeforePlugin("nadena.dev.modular-avatar")
                .Run("Remove authoring controllers from build clone", ctx => StripControllers(ctx.AvatarRootObject));
        }
    }
}
