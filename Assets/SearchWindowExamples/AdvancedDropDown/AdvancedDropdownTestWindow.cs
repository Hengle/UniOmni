using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.IMGUI.Controls;

[Serializable]
public class AdvancedDropdownTestWindow : EditorWindow
{
    [MenuItem("Advanced Dropdown/Show test window")]
    public static void ShowWindow()
    {
        var w = GetWindow<AdvancedDropdownTestWindow>();
        w.Show();
    }

    private static string[] s_DisplayedContentStringArrayShort;
    private static string[] s_DisplayedContentStringArrayLong;
    private static string[] s_DisplayedContentStringArray;
    private static GUIContent[] s_DisplayedContentGUIContent;
    private static int[] s_OptionValuesStringArray;
    private static string[] m_SearchablePopupValuesFlat;
    private static string[] m_SearchablePopupValuesMultilevel;
    private static MyDropdown m_AdvancedDropdown;
    private static WeekdaysDropdown m_AdvancedDropdown2;

    private void OnEnable()
    {
        var stringValues = new List<string>();
        var guiContentValues = new List<GUIContent>();
        var intValues = new List<int>();
        for (int i = 0; i < 100; i++)
        {
            var value =  100 - i;
            intValues.Add(value);
            var text = "Option " + i + " [" + value + "]";
            stringValues.Add(text);
            guiContentValues.Add(new GUIContent(text, Instantiate(i % 2 == 0 ? Texture2D.whiteTexture : Texture2D.blackTexture)));
        }

        s_DisplayedContentStringArray = stringValues.ToArray();
        s_DisplayedContentGUIContent = guiContentValues.ToArray();
        s_OptionValuesStringArray = intValues.ToArray();

        s_DisplayedContentStringArrayShort = new[]
        {
            "One",
            "Two222",
            "Three"
        };

        s_DisplayedContentStringArrayLong = new[]
        {
            "One",
            "SuperSuperSuperSuperSuperSuperSuperSuperSuperSuperSuperSuperSuperSuperSuperSuperSuperLongTwo",
            "Three"
        };

        var options2 = new List<string>();
        var options3 = new List<string>();

        for (int i = 0; i < 500; i += 7)
        {
            var text = "Option " + i + " (" + GetNumberInWords(i) + ")";
            var text2 = "" + i + " (" + GetNumberInWords(i) + ")/";
            options2.Add(text);
            for (int j = 0; j < 400; j += 17)
            {
                var text3 = "" + j + " (" + GetNumberInWords(j) + ")/";
                for (int k = 0; k < 10; k += 1)
                {
                    var text4 = "" + k + " (" + GetNumberInWords(k) + ")";
                    options3.Add(text2 + text3 +  text4);
                }
            }
        }
        m_SearchablePopupValuesFlat = options2.ToArray();
        m_SearchablePopupValuesMultilevel = options3.ToArray();
        m_AdvancedDropdown = new MyDropdown(m_State);
        m_AdvancedDropdown2 = new WeekdaysDropdown(m_State2);

        wantsMouseMove = true;
    }

    public void OnInspectorUpdate()
    {
        // This will only get called 10 times per second.
        Repaint();
    }

    string[] numberMap = new[] { "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine" };

    string GetNumberInWords(int i)
    {
        string result = "";
        do
        {
            result += numberMap[i % 10] + " ";
            i = i / 10;
        }
        while (i > 0);

        return result.Trim();
    }

    [SerializeField]
    private int m_SelectedIndexPopup1;
    [SerializeField]
    private int m_SelectedIndexPopup2;
    [SerializeField]
    private int m_SelectedIndexPopup3;
    [SerializeField]
    private int m_SelectedIndexPopup4;
    [SerializeField]
    private MyEnum m_EnumFlagsPopupSelected;
    [SerializeField]
    private int m_SelectedIntValue = -1;
    [SerializeField]
    private int m_SelectedIntValue2 = -1;
    [SerializeField]
    private int m_Mask;
    [SerializeField]
    private MyEnumFlags m_EnumFlagsFlagMask;
    [SerializeField]
    private int m_SearchablePopupResult1;
    [SerializeField]
    private int m_SearchablePopupResult2;
    [SerializeField]
    private int m_SearchablePopupResult3;
    [SerializeField]
    private int m_ElementsNumber = 40;
    [SerializeField]
    private int m_ElementsNumberIdx;
    [SerializeField]
    private Vector2 m_Scroll;
    [SerializeField]
    AdvancedDropdownState m_State = new AdvancedDropdownState();
    [SerializeField]
    AdvancedDropdownState m_State2 = new AdvancedDropdownState();

    void OnGUI()
    {
        m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);

        EditorGUILayout.LabelField("Window: " + Event.current.mousePosition);
        EditorGUILayout.LabelField("Screen: " + GUIUtility.GUIToScreenPoint(Event.current.mousePosition));

        var r = GUILayoutUtility.GetRect(new GUIContent("Show"), EditorStyles.miniButton);
        if (GUI.Button(r, new GUIContent("Show"), EditorStyles.miniButton))
        {
            m_AdvancedDropdown.Show(r);
        }

        r = GUILayoutUtility.GetRect(new GUIContent("Show weekdays"), EditorStyles.miniButton);
        if (GUI.Button(r, new GUIContent("Show"), EditorStyles.miniButton))
        {
            m_AdvancedDropdown2.Show(r);
        }

        EditorGUILayout.EndScrollView();
    }

    [Flags]
    enum MyEnumFlags
    {
        MyEnum1 = 1,
        MyEnum2 = 2,
        AnEnumWithValue16 = 16,
        MyEnum8 = 8,
        SomeOtherEnumWithValue16 = 16,
        TheLastEnum = 32,
        SuperSuperSuperSuperSuperSuperSuperSuperSuperSuperSuperSuperSuperSuperSuperSuperSuperLongEnum = 64,
    }

    enum MyEnum
    {
        MyEnum1,
        MyEnum2,
        MyEnum3,
        MyEnum8,
    }

    enum MyLongEnum
    {
        MyEnum00,
        MyEnum01,
        MyEnum02,
        MyEnum03,
        MyEnum04,
        MyEnum05,
        MyEnum06,
        MyEnum07,
        MyEnum08,
        MyEnum09,
        MyEnum10,
        MyEnum11,
        MyEnum12,
        MyEnum13,
        MyEnum14,
        MyEnum15,
        MyEnum16,
        MyEnum17,
        MyEnum18,
        MyEnum19,
        SuperSuperSuperSuperSuperSuperSuperSuperSuperSuperSuperSuperSuperSuperSuperSuperSuperLongEnum,
        MyEnum20,
        MyEnum21,
        MyEnum22,
        MyEnum23,
        MyEnum24,
        MyEnum25,
        MyEnum26,
        MyEnum27,
        MyEnum28,
        MyEnum29,
        MyEnum30,
    }


    class MyDropdown : AdvancedDropdown
    {
        public MyDropdown(AdvancedDropdownState state) : base(state)
        {
            // This is internal api: 
            // m_DataSource = new MultiLevelDataSource(m_SearchablePopupValuesMultilevel);
        }

        protected override AdvancedDropdownItem BuildRoot()
        {
            throw new NotImplementedException();
        }
    }
    class WeekdaysDropdown : AdvancedDropdown
    {
        public WeekdaysDropdown(AdvancedDropdownState state) : base(state)
        {
        }

        protected override AdvancedDropdownItem BuildRoot()
        {
            var root = new AdvancedDropdownItem("Weekdays");

            var firstHalf = new AdvancedDropdownItem("First half");
            var secondHalf = new AdvancedDropdownItem("Second half");
            var weekend = new AdvancedDropdownItem("Weekend");

            firstHalf.AddChild(new AdvancedDropdownItem("Monday"));
            firstHalf.AddChild(new AdvancedDropdownItem("Tuesday"));
            secondHalf.AddChild(new AdvancedDropdownItem("Wednesday"));
            secondHalf.AddChild(new AdvancedDropdownItem("Thursday"));
            weekend.AddChild(new AdvancedDropdownItem("Friday"));
            weekend.AddChild(new AdvancedDropdownItem("Saturday"));
            weekend.AddChild(new AdvancedDropdownItem("Sunday"));

            root.AddChild(firstHalf);
            root.AddChild(secondHalf);
            root.AddChild(weekend);

            return root;
        }
    }
}
