using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Agxmeister.Uplink.Hierarchy
{
    /// <summary>
    /// Turns a component's serialized fields into JSON.
    ///
    /// Reading them through <see cref="SerializedObject"/> rather than by reflecting over the component's own
    /// type is what makes this work for any script in any project: Unity has already decided what is a field
    /// worth showing, and this shows the same ones the Inspector does.
    /// </summary>
    public static class SerializedValues
    {
        public static IDictionary<string, object> Of(Object component)
        {
            var values = new Dictionary<string, object>();
            var serialized = new SerializedObject(component);
            var property = serialized.GetIterator();

            var enterChildren = true;
            while (property.NextVisible(enterChildren))
            {
                // Top level only: a deep component would otherwise flatten into hundreds of entries, and the
                // structured types below already carry what is worth knowing.
                enterChildren = false;

                if (property.name == "m_Script")
                {
                    continue;
                }
                values[property.name] = Value(property);
            }

            serialized.Dispose();
            return values;
        }

        private static object Value(SerializedProperty property)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                    return property.intValue;
                case SerializedPropertyType.Boolean:
                    return property.boolValue;
                case SerializedPropertyType.Float:
                    return property.floatValue;
                case SerializedPropertyType.String:
                    return property.stringValue;
                case SerializedPropertyType.Enum:
                    return EnumName(property);
                case SerializedPropertyType.ObjectReference:
                    return Reference(property.objectReferenceValue);
                case SerializedPropertyType.Vector2:
                    return new Dictionary<string, object> { { "x", property.vector2Value.x }, { "y", property.vector2Value.y } };
                case SerializedPropertyType.Vector3:
                    return Xyz(property.vector3Value);
                case SerializedPropertyType.Vector4:
                    var vector4 = property.vector4Value;
                    return new Dictionary<string, object>
                    {
                        { "x", vector4.x }, { "y", vector4.y }, { "z", vector4.z }, { "w", vector4.w },
                    };
                case SerializedPropertyType.Quaternion:
                    // Reported as the Euler angles the Inspector shows, which is what anyone reasons about.
                    return Xyz(property.quaternionValue.eulerAngles);
                case SerializedPropertyType.Color:
                    var color = property.colorValue;
                    return new Dictionary<string, object>
                    {
                        { "r", color.r }, { "g", color.g }, { "b", color.b }, { "a", color.a },
                    };
                case SerializedPropertyType.Rect:
                    var rect = property.rectValue;
                    return new Dictionary<string, object>
                    {
                        { "x", rect.x }, { "y", rect.y }, { "width", rect.width }, { "height", rect.height },
                    };
                case SerializedPropertyType.Bounds:
                    var bounds = property.boundsValue;
                    return new Dictionary<string, object>
                    {
                        { "center", Xyz(bounds.center) }, { "size", Xyz(bounds.size) },
                    };
                case SerializedPropertyType.LayerMask:
                    return property.intValue;
                case SerializedPropertyType.Character:
                    return property.intValue;
                case SerializedPropertyType.AnimationCurve:
                    return Unrepresented("AnimationCurve");
                default:
                    // Arrays, nested structs and the rest: named rather than dropped, so a client can see
                    // that something is there without this having to model every shape Unity can serialize.
                    return property.isArray && property.propertyType != SerializedPropertyType.String
                        ? new Dictionary<string, object>
                        {
                            { "type", property.arrayElementType },
                            { "count", property.arraySize },
                        }
                        : Unrepresented(property.propertyType.ToString());
            }
        }

        private static IDictionary<string, object> Xyz(Vector3 value)
        {
            return new Dictionary<string, object> { { "x", value.x }, { "y", value.y }, { "z", value.z } };
        }

        private static object EnumName(SerializedProperty property)
        {
            var names = property.enumDisplayNames;
            var index = property.enumValueIndex;
            return index >= 0 && index < names.Length ? names[index] : (object)index;
        }

        private static object Reference(Object referenced)
        {
            if (referenced == null)
            {
                return null;
            }

            return new Dictionary<string, object>
            {
                { "type", referenced.GetType().Name },
                { "name", referenced.name },
            };
        }

        private static IDictionary<string, object> Unrepresented(string type)
        {
            return new Dictionary<string, object> { { "type", type } };
        }
    }
}
