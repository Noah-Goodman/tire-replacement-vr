using UnityEngine;

namespace TireTraining
{
    public class LugNutLandingDetector : MonoBehaviour
    {
        public bool Landed { get; private set; }

        private void OnCollisionEnter(Collision collision)
        {
            if (collision.collider.gameObject.name == "Floor")
            {
                Landed = true;
            }
        }
    }
}
