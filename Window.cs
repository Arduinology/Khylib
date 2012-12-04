using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Khylib
{
    public class Window
    {
        private const int BaseWindowId = 5414;
        private static readonly List<Window> AllocatedWindows = new List<Window>();

        public static void DrawAll()
        {
            GUI.skin = HighLogic.Skin;
            foreach (var window in AllocatedWindows.Where(w => w.IsRendered && w.IsActive))
            {
                window.WindowRect = string.IsNullOrEmpty(window.Title) ?
                                        GUILayout.Window(window._thisWindowId, window.WindowRect, window.DrawWindow, GUIContent.none, window.WindowOptions) :
                                        GUILayout.Window(window._thisWindowId, window.WindowRect, window.DrawWindow, window.Title, window.WindowOptions);
            }
        }

        public static void CloseAll()
        {
            AllocatedWindows.Clear();
        }

        public Rect WindowRect { get; set; }
        public string Title { get; set; }
        public bool IsRendered { get; set; }
        public virtual bool IsActive { get { return true; } }
        public List<IWindowContent> Contents { get; set; }
        public event Action Draw;
        private readonly int _thisWindowId;

        public Window(string title)
        {
            Title = title;
            _thisWindowId = BaseWindowId;
            IsRendered = true;
            while (AllocatedWindows.Any(w => w._thisWindowId == _thisWindowId))
                _thisWindowId++;
            AllocatedWindows.Add(this);
        }

        public void CloseWindow()
        {
            IsRendered = false;
            AllocatedWindows.Remove(this);
        }

        private void DrawWindow(int id)
        {
            if (Draw != null)
            {
                Draw();
            }
            if (Contents != null)
            {
                GUILayout.BeginVertical();
                foreach (var content in Contents)
                    content.Draw();
                GUILayout.EndVertical();
            }
            GUI.DragWindow();
        }

        public T2 FindField<T1, T2>(string key) where T1 : class, IValueHolder<T2>, IWindowContent
        {
            if (Contents == null)
                return default(T2);
            foreach (var content in Contents.OfType<T1>().Where(content => content.Name == key))
                return content.Value;
            return default(T2);
        }

        public void SetField<T1, T2>(string key, T2 value) where T1 : class, IValueHolder<T2>, IWindowContent
        {
            if (Contents == null)
                return;
            foreach (var content in Contents.OfType<T1>().Where(content => content.Name == key))
            {
                content.Value = value;
                return;
            }
            MonoBehaviour.print("HyperEdit error: SetField key '" + key + "' not found");
        }

        protected virtual GUILayoutOption[] WindowOptions
        {
            get { return new[] { GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true) }; }
        }
    }

    public class Selector<T> : Window
    {
        public Selector(string title, IEnumerable<T> elements, Func<T, string> nameSelector, Action<T> onSelect)
            : base(title)
        {
            WindowRect = new Rect(Screen.width / 4 - 125, Screen.height / 2 - 200, 250, 400);
            Contents = new List<IWindowContent>
                           {
                               new Scroller(elements.Select(a =>
                                                            (IWindowContent) new CustomDisplay(() =>
                                                                                                   {
                                                                                                       if (!GUILayout.Button(nameSelector(a))) return;
                                                                                                       onSelect(a);
                                                                                                       CloseWindow();
                                                                                                   })).ToArray()),
                               new Button("Cancel", CloseWindow)
                           };
        }
    }

    public class PopupWindow : Window
    {
        public PopupWindow(string message, string title, int width, int height)
            : base(title)
        {
            Contents = new List<IWindowContent>
                           {
                               new Label(message),
                               new Button("Close", CloseWindow)
                           };
            WindowRect = new Rect(Screen.width / 2 - width / 2, Screen.height / 2 - height / 2, width, height);
        }
    }

    public interface IWindowContent
    {
        void Draw();
    }

    public interface IValueHolder<T>
    {
        string Name { get; set; }
        T Value { get; set; }
    }

    public class Label : IWindowContent
    {
        public string Text { get; set; }

        public Label(string text)
        {
            Text = text;
        }

        public void Draw()
        {
            GUILayout.Label(Text);
        }
    }

    public class TextBox : IWindowContent, IValueHolder<string>
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public Action<string> OnPress { get; set; }

        public TextBox(string fieldName, string textValue)
        {
            Name = fieldName;
            Value = textValue;
        }

        public TextBox(string fieldName, string textValue, Action<string> onPress)
        {
            Name = fieldName;
            Value = textValue;
            OnPress = onPress;
        }

        public void Draw()
        {
            GUILayout.BeginHorizontal();
            if (string.IsNullOrEmpty(Name) == false)
                GUILayout.Label(Name);
            Value = GUILayout.TextField(Value);
            if (OnPress != null && GUILayout.Button("Set"))
                OnPress(Value);
            GUILayout.EndHorizontal();
        }
    }

    public class Button : IWindowContent
    {
        public string Text { get; set; }
        public Action OnClick { get; set; }

        public Button(string text, Action onClick)
        {
            Text = text;
            OnClick = onClick;
        }

        public void Draw()
        {
            if (GUILayout.Button(Text))
                OnClick();
        }
    }

    public class Toggle : IWindowContent, IValueHolder<bool>
    {
        public string Name { get; set; }
        public Action<bool> OnChange { get; set; }
        public bool Value { get; set; }

        public Toggle(string text, Action<bool> onChange)
        {
            Name = text;
            OnChange = onChange;
        }

        public void Draw()
        {
            var prev = Value;
            Value = GUILayout.Toggle(Value, Name);
            if (prev != Value)
                OnChange(Value);
        }
    }

    public class Scroller : IWindowContent
    {
        public IWindowContent[] Contents { get; set; }
        public Vector2 Position { get; set; }

        public Scroller(IWindowContent[] contents)
        {
            Contents = contents;
        }

        public void Draw()
        {
            Position = GUILayout.BeginScrollView(Position);
            foreach (var windowContent in Contents)
                windowContent.Draw();
            GUILayout.EndScrollView();
        }
    }

    public class Slider : IWindowContent, IValueHolder<float>
    {
        public string Name { get; set; }
        public float Min { get; set; }
        public float Max { get; set; }
        public float Value { get; set; }
        public Action<float> OnChange { get; set; }

        public Slider(string name, float min, float max, float value, Action<float> onChange)
        {
            Name = name;
            Min = min;
            Max = max;
            Value = value;
            OnChange = onChange;
        }

        public void Draw()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(Name);
            var prevValue = Value;
            Value = GUILayout.HorizontalSlider(Value, Min, Max);
            if (Math.Abs(Value - prevValue) > 1E-5f)
                OnChange(Value);
            GUILayout.EndHorizontal();
        }
    }

    public class CustomDisplay : IWindowContent
    {
        public Action DrawFunc { get; set; }

        public CustomDisplay(Action drawFunc)
        {
            DrawFunc = drawFunc;
        }

        public void Draw()
        {
            DrawFunc();
        }
    }

}
