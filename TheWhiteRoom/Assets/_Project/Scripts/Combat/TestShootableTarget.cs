using UnityEngine;

namespace TWR.Combat
{
    /// <summary>
    /// TEST ONLY: a puzzle-style target that cycles its colour when shot (D-118; D-036, D-105).
    /// Stand-in for the step-3 switch. The "requirement" field shows the gun filter:
    /// - default (requireSpecificGun off) = ANY gun triggers it, like a normal switch;
    /// - a pistol-only switch: tick requireSpecificGun and set requiredGun = Pistol.
    /// The test scene has one "any gun" target and one "Revolver only" target that the pistol can't trigger
    /// (it ignores the shot: no colour change, no hit marker).
    /// </summary>
    public class TestShootableTarget : MonoBehaviour, IShootable
    {
        [SerializeField] GunRequirement requirement = GunRequirement.Any;
        [SerializeField] Color[] colours = { new Color(0.2f, 0.5f, 1f), new Color(1f, 0.8f, 0.1f), new Color(0.2f, 1f, 0.3f) };
        [SerializeField] Renderer targetRenderer;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        MaterialPropertyBlock block;
        int colourIndex;

        public GunRequirement Requirement => requirement;

        void Awake()
        {
            if (targetRenderer == null) targetRenderer = GetComponent<Renderer>();
            block = new MaterialPropertyBlock();
            ApplyColour();
        }

        public bool OnShot(in ShotHitInfo hit)
        {
            if (!requirement.Accepts(hit.Gun)) return false; // wrong gun: ignore the shot

            if (colours == null || colours.Length == 0) return true;
            colourIndex = (colourIndex + 1) % colours.Length;
            ApplyColour();
            return true;
        }

        void ApplyColour()
        {
            if (targetRenderer == null || colours == null || colours.Length == 0) return;
            Color c = colours[colourIndex];
            targetRenderer.GetPropertyBlock(block);
            block.SetColor(BaseColorId, c);
            block.SetColor(EmissionColorId, c * 0.6f);
            targetRenderer.SetPropertyBlock(block);
        }
    }
}
