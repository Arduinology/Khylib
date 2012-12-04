using UnityEngine;

namespace Khylib
{
    public static class Immortal
    {
        private static GameObject _gameObject;
        public static void AddImmortal<T>() where T : Component
        {
            if (_gameObject == null)
            {
                _gameObject = new GameObject("KhylibImmortal", typeof(WindowDrawer), typeof(T));
                Object.DontDestroyOnLoad(_gameObject);
            }
            else if (_gameObject.GetComponent<T>() == null)
                _gameObject.AddComponent<T>();
        }
    }

    public class WindowDrawer : MonoBehaviour
    {
// ReSharper disable InconsistentNaming
        public void OnGUI()
// ReSharper restore InconsistentNaming
        {
            Window.DrawAll();
        }
    }
}
