using System.Collections;
using UnityEngine;

namespace NaughtyAttributes.Test
{
    public class ShootTest : MonoBehaviour
    {
         public Weapon weapon;
        [Button(enabledMode: EButtonEnableMode.Always)]
        private void TestShoot()
        {
            weapon.Shoot();
        }
    }
}



