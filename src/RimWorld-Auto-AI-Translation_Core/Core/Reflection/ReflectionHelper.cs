using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;
using RimWorld;
// 這個檔案負責反射輔助工具。
// EN: This file provides reflection helper utilities.

namespace AutoTranslator_Core
{


    // 這個類別負責 反射輔助 的主要流程與狀態。
    // EN: This class manages the main workflow and state for ReflectionHelper.
    public static class ReflectionHelper
    {
        // 這個欄位保存 GetPropertyValue 的執行狀態或快取資料。
        // EN: This field stores get property value runtime state or cached data.
        public static T GetPropertyValue<T>(object obj, params string[] propertyNames) where T : class
        {
            if (obj == null) return null;
            Type type = obj.GetType();
            foreach (var name in propertyNames)
            {
                try
                {
                    var prop = type.GetProperty(name);
                    if (prop != null)
                    {
                        var val = prop.GetValue(obj);
                        if (val is T typed && !string.IsNullOrWhiteSpace(val.ToString()))
                            return typed;
                    }
                }
                catch { }
            }
            return null;
        }
    }
}
