using System;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ItemStatsSystem;
using UnityEngine;

namespace AutoM14
{
    public class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private Type slotType = null;
        private ConstructorInfo slotConstructor = null;
        private Dictionary<string, object> slotTemplateData = new Dictionary<string, object>();
        private HashSet<int> processedItems = new HashSet<int>();

        void Start()
        {
            StartCoroutine(DelayedInit());
        }

        private IEnumerator DelayedInit()
        {
            yield return new WaitForSeconds(1f);
            
            try
            {
                AnalyzeSlotStructure();
            }
            catch { }
        }

        private void AnalyzeSlotStructure()
        {
            try
            {
                var reference = ItemAssetsCollection.InstantiateSync(786);
                if (reference == null) return;
                
                var refSlots = reference.GetType().GetProperty("Slots").GetValue(reference);
                if (refSlots == null)
                {
                    Destroy(reference.gameObject);
                    return;
                }
                
                if (refSlots is IEnumerable refEnum)
                {
                    foreach (var slot in refEnum)
                    {
                        if (slot == null) continue;
                        
                        if (slotType == null)
                        {
                            slotType = slot.GetType();
                            slotConstructor = slotType.GetConstructor(Type.EmptyTypes);
                            if (slotConstructor == null)
                            {
                                var constructors = slotType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                slotConstructor = constructors.Length > 0 ? constructors[0] : null;
                            }
                        }
                        
                        var keyProp = slot.GetType().GetProperty("Key");
                        if (keyProp != null)
                        {
                            var key = keyProp.GetValue(slot) as string;
                            if (key == "Stock" || key == "Tec")
                            {
                                var slotData = new Dictionary<string, object>();
                                var fields = slotType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        
                                foreach (var field in fields)
                                {
                                    try
                                    {
                                        slotData[field.Name] = field.GetValue(slot);
                                    }
                                    catch { }
                                }
                        
                                slotTemplateData[key] = slotData;
                            }
                        }
                    }
                }
                
                Destroy(reference.gameObject);
            }
            catch { }
        }

        private object CreateNewSlot(string slotKey)
        {
            if (slotType == null || !slotTemplateData.ContainsKey(slotKey)) return null;
            
            try
            {
                object newSlot = null;
                
                if (slotConstructor != null)
                {
                    var parameters = slotConstructor.GetParameters();
                    if (parameters.Length == 0)
                    {
                        newSlot = slotConstructor.Invoke(null);
                    }
                    else
                    {
                        var defaultParams = new object[parameters.Length];
                        for (int i = 0; i < parameters.Length; i++)
                        {
                            var paramType = parameters[i].ParameterType;
                            defaultParams[i] = paramType.IsValueType ? Activator.CreateInstance(paramType) : null;
                        }
                        newSlot = slotConstructor.Invoke(defaultParams);
                    }
                }
                
                if (newSlot == null) return null;
                
                var templateData = slotTemplateData[slotKey] as Dictionary<string, object>;
                if (templateData != null)
                {
                    var fields = slotType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    foreach (var field in fields)
                    {
                        string fieldNameLower = field.Name.ToLower();
                        if (fieldNameLower.Contains("instanceid") || fieldNameLower.Contains("objectid") || fieldNameLower == "m_instanceid")
                        {
                            continue;
                        }
                        
                        if (templateData.ContainsKey(field.Name))
                        {
                            try
                            {
                                field.SetValue(newSlot, templateData[field.Name]);
                            }
                            catch { }
                        }
                    }
                }
                
                return newSlot;
            }
            catch
            {
                return null;
            }
        }

        void Update()
        {
            if (Time.frameCount % 60 != 0 || slotType == null || slotTemplateData.Count == 0) return;
            
            try
            {
                var allItems = FindObjectsOfType<Item>();
                foreach (var item in allItems)
                {
                    if (item.TypeID == 787 && !processedItems.Contains(item.GetInstanceID()))
                    {
                        processedItems.Add(item.GetInstanceID());
                        ModifyM14(item);
                    }
                }
                
                processedItems.RemoveWhere(id => allItems.All(i => i.GetInstanceID() != id));
            }
            catch { }
        }

        private void ModifyM14(Item item)
        {
            try
            {
                // 修改射击模式为全自动
                var components = item.GetComponents<Component>();
                foreach (var component in components)
                {
                    if (component == null) continue;

                    var field = component.GetType().GetField("triggerMode", 
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                    if (field != null && field.FieldType.IsEnum)
                    {
                        var enumNames = Enum.GetNames(field.FieldType);
                        var enumValues = Enum.GetValues(field.FieldType);

                        for (int i = 0; i < enumNames.Length; i++)
                        {
                            if (enumNames[i].ToLower() == "auto")
                            {
                                field.SetValue(component, enumValues.GetValue(i));
                                break;
                            }
                        }
                        break;
                    }
                }
                
                // 添加缺失的槽位
                var slots = item.GetType().GetProperty("Slots").GetValue(item);
                if (slots == null) return;
                
                var existingKeys = new HashSet<string>();
                if (slots is IEnumerable enumSlots)
                {
                    foreach (var slot in enumSlots)
                    {
                        if (slot != null)
                        {
                            var keyProp = slot.GetType().GetProperty("Key");
                            if (keyProp != null)
                            {
                                var key = keyProp.GetValue(slot) as string;
                                if (!string.IsNullOrEmpty(key))
                                {
                                    existingKeys.Add(key);
                                }
                            }
                        }
                    }
                }
                
                var slotsType = slots.GetType();
                var addMethod = slotsType.GetMethod("Add");
                
                if (addMethod != null)
                {
                    // 添加 Stock 枪托槽位
                    if (!existingKeys.Contains("Stock") && slotTemplateData.ContainsKey("Stock"))
                    {
                        var newSlot = CreateNewSlot("Stock");
                        if (newSlot != null)
                        {
                            var collectionField = slotType.GetField("collection", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            if (collectionField != null)
                            {
                                collectionField.SetValue(newSlot, slots);
                            }
                            
                            try
                            {
                                addMethod.Invoke(slots, new object[] { newSlot });
                            }
                            catch { }
                        }
                    }
                    
                    // 添加 Tec 战术槽位
                    if (!existingKeys.Contains("Tec") && slotTemplateData.ContainsKey("Tec"))
                    {
                        var newSlot = CreateNewSlot("Tec");
                        if (newSlot != null)
                        {
                            var collectionField = slotType.GetField("collection", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            if (collectionField != null)
                            {
                                collectionField.SetValue(newSlot, slots);
                            }
                            
                            try
                            {
                                addMethod.Invoke(slots, new object[] { newSlot });
                            }
                            catch { }
                        }
                    }
                }
            }
            catch { }
        }
        
        void OnDestroy()
        {
            processedItems.Clear();
        }
    }
}
