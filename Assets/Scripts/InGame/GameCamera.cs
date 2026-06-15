using UnityEngine;
using System.Collections;

public class GameCamera : MonoBehaviour
{
   [SerializeField] private float rotationSpeed = 2.0f; // 回転にかかる時間（秒）
    private Coroutine currentRotation;
    // 指定した角度へスムーズに回転させる
    public void RotateToPlayer(PlayerType player)
    {
        float targetZ = (player == PlayerType.Player1) ? 0f : 180f;
        if (currentRotation != null) StopCoroutine(currentRotation);
        currentRotation = StartCoroutine(RotateRoutine(targetZ));
        
    }

    private IEnumerator RotateRoutine(float targetZ)
    {
        Quaternion startRot = transform.rotation;
        Quaternion endRot = Quaternion.Euler(0, 0, targetZ);
        float elapsed = 0;

        while (elapsed < rotationSpeed)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / rotationSpeed;
            // 動きを滑らかにする（イージング）
            t = t * t * (3f - 2f * t); 
            transform.rotation = Quaternion.Lerp(startRot, endRot, t);
            yield return null;
        }
        transform.rotation = endRot;
    }
}
