using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;

namespace Omni
{
    public class OmniItem
    {
        public string Name { get; set; }

    }

    public abstract class OmniProvider
    {
        public abstract List<OmniItem> GetItems();
        public abstract void Execute(OmniItem item);
    }

    public class OmniTool : PopupWindowContent
    {
        public override void OnGUI(Rect rect)
        {
            
        }

        public override Vector2 GetWindowSize()
        {
            return new Vector2(750, 600f);
        }

        public override void OnOpen()
        {
        }

        public override void OnClose()
        {
        }

        [Shortcut("Window/Omni Tool", KeyCode.O, ShortcutModifiers.Alt | ShortcutModifiers.Shift)]
        public static void PopOmniTool()
        {
            PopupWindow.Show(new Rect(100, 100, 400, 800), new OmniTool());

        }
    }
}

