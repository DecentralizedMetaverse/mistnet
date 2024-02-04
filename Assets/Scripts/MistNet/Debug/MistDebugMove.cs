using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MistNet
{
    public class MistDebugMove : MonoBehaviour
    {
        private static int AreaSize = 1000;
        [SerializeField] private float maxSpeed = 1f;
        private Vector3 _moveVector = Vector3.zero;
        
        private void Start()
        {
            var x = Random.Range(-maxSpeed, maxSpeed);
            var y = Random.Range(-maxSpeed, maxSpeed);
            var z = Random.Range(-maxSpeed, maxSpeed);
            _moveVector = new Vector3(x, y, z);
        }
        
        private void Update()
        {
            transform.position += _moveVector;
            
            if (transform.position.x > AreaSize || transform.position.x < -AreaSize) _moveVector.x = -_moveVector.x;
            if (transform.position.y > AreaSize || transform.position.y < -AreaSize) _moveVector.y = -_moveVector.y;
            if (transform.position.z > AreaSize || transform.position.z < -AreaSize) _moveVector.z = -_moveVector.z;
        }
    }
}