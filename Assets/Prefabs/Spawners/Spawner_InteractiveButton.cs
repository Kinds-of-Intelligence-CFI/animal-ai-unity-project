using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Spawner_InteractiveButton : MonoBehaviour
{
    public SpawnerStockpiler spawnerStockpiler; // reference to the spawner script (SpawnerStockpiler.cs)
    public SpawnerStockpiler spawnerDisperser; // reference to the spawner script (SpawnerDisperser.cs)
    public GameObject childObjectToMove; // reference to the child object you want to move
    public Vector3 moveOffset; // the offset values to apply
    public float moveDuration = 1f; // duration of the move animation in seconds
    public float resetDuration = 1f; // duration of the reset animation in seconds

    private void OnTriggerEnter(Collider other) // when the agent enters the trigger zone (button)
    {
        if (other.CompareTag("agent")) // if the agent enters the trigger zone (button)
        {
            Debug.Log("trigger activated"); // print "Button pressed" in the console
            spawnerStockpiler.triggerActivated = true; // then enable the spawner script (SpawnerStockpiler.cs)
            spawnerDisperser.triggerActivated = true; // then enable the spawner script (SpawnerDisperser.cs)

            // Start the MoveAndReset coroutine
            StartCoroutine(MoveAndReset());
        }
        else
        {
            Debug.Log("trigger not activated"); // print "Button not pressed" in the console
        }
    }

    private IEnumerator MoveAndReset()
    {
        // Store the original position
        Vector3 originalPosition = childObjectToMove.transform.position;

        // Move the child object
        Vector3 targetPosition = originalPosition + moveOffset;
        float startTime = Time.time;
        while (Time.time < startTime + moveDuration)
        {
            float t = (Time.time - startTime) / moveDuration;
            childObjectToMove.transform.position = Vector3.Lerp(originalPosition, targetPosition, t);
            yield return null;
        }
        childObjectToMove.transform.position = targetPosition;

        // Wait for a moment (optional, remove this line if not needed)
        // yield return new WaitForSeconds(1f);

        // Reset the position
        startTime = Time.time;
        while (Time.time < startTime + resetDuration)
        {
            float t = (Time.time - startTime) / resetDuration;
            childObjectToMove.transform.position = Vector3.Lerp(targetPosition, originalPosition, t);
            yield return null;
        }
        childObjectToMove.transform.position = originalPosition;
    }
}
