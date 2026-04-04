using UnityEngine;
using UnityEngine.UI;

public class DashChargeUI : MonoBehaviour
{
    public PlayerZeroGMovement playerMovement;
    public Image[] chargeImages;

    void Update()
    {
        if (playerMovement == null || chargeImages == null)
        {
            return;
        }

        int charges = playerMovement.CurrentDashCharges;
        int max = playerMovement.maxDashCharges;
        float rechargeProgress = playerMovement.DashRechargeProgress;

        for (int i = 0; i < chargeImages.Length; i++)
        {
            if (i < charges)
            {
                chargeImages[i].fillAmount = 1f;
            }
            else if (i == charges && charges < max)
            {
                chargeImages[i].fillAmount = rechargeProgress;
            }
            else
            {
                chargeImages[i].fillAmount = 0f;
            }
        }
    }
}
