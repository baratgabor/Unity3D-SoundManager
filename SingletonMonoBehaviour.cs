using System;
using UnityEngine;

namespace LeakyAbstraction
{
    [DisallowMultipleComponent]
    public abstract class SingletonMonoBehaviour<T> : MonoBehaviour where T : SingletonMonoBehaviour<T>
    {
        public static T Instance => _instance ?? throw new InvalidOperationException($"Pre-instantiation missing. An instance of {typeof(T).Name} is required to exist in the scene.");
        protected static T _instance;

        protected virtual void Awake()
        {
            if (_instance == null)
                _instance = (T)this;
            else if (_instance != this)
                Destroy(this);
        }
    }
}