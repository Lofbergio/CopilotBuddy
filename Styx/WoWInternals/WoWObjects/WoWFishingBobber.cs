using System;

namespace Styx.WoWInternals.WoWObjects
{
    /// <summary>
    /// Représente un bobber de pêche dans WoW.
    /// WoW 3.3.5a build 12340.
    /// </summary>
    public class WoWFishingBobber : WoWAnimatedSubObject
    {
        internal WoWFishingBobber(uint baseAddress) : base(baseAddress)
        {
        }

        /// <summary>
        /// Indique si le bobber est en train de bouger (poisson qui mord).
        /// AnimationState == 8 signifie que le poisson a mordu.
        /// </summary>
        public bool IsBobbing => AnimationState == 8;
    }
}
