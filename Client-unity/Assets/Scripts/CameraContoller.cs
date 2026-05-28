using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraContoller : MonoBehaviour
{
    public static CameraContoller Instance { get; private set; }
    private Transform followTarget;
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }
    public void SetFollowTarget(Transform target)
    {
        followTarget = target;
    }

    // Update is called once per frame
    void Update()
    {
        if(followTarget == null) return;
        
        Vector3 targetPosition = new Vector3(followTarget.position.x, followTarget.position.y, transform.position.z);
        transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * 3f);
    }
}
