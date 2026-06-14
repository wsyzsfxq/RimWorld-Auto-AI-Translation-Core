using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;
using RimWorld;

namespace AutoTranslator_Core
{
    /// <summary>
    /// 安全地透過反射讀取屬性值，跨版本相容
    /// 用法：var author = ReflectionHelper.GetPropertyValue<string>(meta, "AuthorsString", "Author");
    /// </summary>
    public static class ReflectionHelper
    {
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
