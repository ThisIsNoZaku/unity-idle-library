using io.github.thisisnozaku.idle.framework.Engine.Persistence;
using io.github.thisisnozaku.idle.framework.Events;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Interop;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using UnityEngine;
namespace io.github.thisisnozaku.idle.framework.Engine
{
    /*
     * Base class for custom types.
     */
    public abstract class Entity : IEventSource, IEngineAware
    {
        [JsonIgnore]
        public IdleEngine Engine { get; private set; }
        public Dictionary<string, bool> Flags { get; private set; }
        [JsonProperty]
        private EventListeners eventListeners;
        [JsonProperty]
        private Dictionary<string, string> propertyCalculationScripts = new Dictionary<string, string>();
        [JsonProperty]
        private List<long> appliedModifiers = new List<long>();
        public long Id { get; }
        /*
         * Return the list of the ids of all modifiers applied to this entity.
         * 
         * Override this method and change it to return null to signal that this entity will not accept modifiers.
         */
        public List<long> GetModifiers() => appliedModifiers;

        public Entity(IdleEngine engine, long id)
        {
            this.Engine = engine;
            this.Id = id;
            
            Flags = new Dictionary<string, bool>();
            this.eventListeners = new EventListeners(engine);
            this.ExtraProperties = new Dictionary<string, object>();
            if (engine != null)
            {
                engine.RegisterEntity(this);
            }
        }

        [OnDeserialized]
        public void OnDeserialization(StreamingContext ctx)
        {
            this.Engine = (IdleEngine)ctx.Context;
            this.Engine.RegisterEntity(this);
        }

        /*
         * Generic method to add a modifier to this entity.
         * 
         * Implement methods which accept domain-specific subclasses of PropertyModifier which forward to this method to handle the heavy lifting.
         */
        public void AddModifier<T>(EntityModifier<T> propertyModifier) where T : Entity
        {
            var modifiers = GetModifiers();
            if (modifiers != null)
            {
                propertyModifier.Apply((T)this);
                modifiers.Add(propertyModifier.Id);
            }
        }

        public void RemoveModifier<T>(EntityModifier<T> propertyModifier) where T : Entity
        {
            var modifiers = GetModifiers();
            if (modifiers != null)
            {
                var removed = modifiers.Remove(propertyModifier.Id);
                if (removed)
                {
                    propertyModifier.Unapply((T)this);
                }
                
            }
        }

        private Dictionary<string, Action> calculatedPropertySetters = new Dictionary<string, Action>();
        public void Update(IdleEngine engine, float deltaTime) { 
            foreach(var child in propertyCalculationScripts)
            {
                if(!calculatedPropertySetters.ContainsKey(child.Key))
                {
                    FieldInfo fieldInfo = GetType().GetField(child.Key);
                    var updateScriptContext = new Dictionary<string, object>()
                    {
                        { "this", this},
                        { "value", null }
                    };
                    if (fieldInfo != null)
                    {
                        calculatedPropertySetters[child.Key] = () =>
                        {
                            object currentValue = fieldInfo.GetValue(this);
                            updateScriptContext["value"] = currentValue;
                            fieldInfo.SetValue(this, Engine.Scripting.Evaluate(child.Value, updateScriptContext).ToObject());
                        };
                    }

                    PropertyInfo propertyInfo = GetType().GetProperty(child.Key);
                    if (propertyInfo != null && !calculatedPropertySetters.ContainsKey(child.Key))
                    {
                        calculatedPropertySetters[child.Key] = () =>
                        {
                            object currentValue = propertyInfo.GetValue(this);
                            updateScriptContext["value"] = currentValue;
                            propertyInfo.SetValue(this, Engine.Scripting.Evaluate(child.Value, updateScriptContext).ToObject());
                        };
                    }
                    if(!calculatedPropertySetters.ContainsKey(child.Key))
                    {
                        throw new InvalidOperationException();
                    }
                    
                }
                calculatedPropertySetters[child.Key]();
            }
            CustomUpdate(engine, deltaTime);
        }

        /*
         * Override this method to implement your own update logic in your custom entities.
         */
        protected virtual void CustomUpdate(IdleEngine engine, float deltaTime) { 

        }

        public void CalculateChild(string childValueSelector, string script)
        {
            propertyCalculationScripts[childValueSelector] = script;
        }


        public void Emit(string eventName, IDictionary<string, object> contextToUse = null)
        {
            eventListeners.Emit(eventName, contextToUse);
        }

        public void Emit(string eventName, ScriptingContext contextToUse)
        {
            eventListeners.Emit(eventName, contextToUse);
        }

        public void Watch(string eventName, string subscriber, string handler)
        {
            ((IEventSource)eventListeners).Watch(eventName, subscriber, handler);
        }

        public void StopWatching(string eventName, string subscriptionTarget)
        {
            ((IEventSource)eventListeners).StopWatching(eventName, subscriptionTarget);
        }

        public void SetFlag(string flag) => Flags[flag] = true;

        public void ClearFlag(string flag) => Flags.Remove(flag);

        public bool GetFlag(string flag)
        {
            return Flags.ContainsKey(flag) && Flags[flag];
        }

        public override bool Equals(object obj)
        {
            return obj is Entity entity &&
                   EqualityComparer<Dictionary<string, bool>>.Default.Equals(Flags, entity.Flags) &&
                   EqualityComparer<Dictionary<string, string>>.Default.Equals(propertyCalculationScripts, entity.propertyCalculationScripts) &&
                   EqualityComparer<List<long>>.Default.Equals(appliedModifiers, entity.appliedModifiers) &&
                   Id == entity.Id &&
                   EqualityComparer<Dictionary<string, object>>.Default.Equals(ExtraProperties, entity.ExtraProperties);
        }

        public override int GetHashCode()
        {
            int hashCode = 36677474;
            hashCode = hashCode * -1521134295 + EqualityComparer<Dictionary<string, bool>>.Default.GetHashCode(Flags);
            hashCode = hashCode * -1521134295 + EqualityComparer<Dictionary<string, string>>.Default.GetHashCode(propertyCalculationScripts);
            hashCode = hashCode * -1521134295 + EqualityComparer<List<long>>.Default.GetHashCode(appliedModifiers);
            hashCode = hashCode * -1521134295 + Id.GetHashCode();
            hashCode = hashCode * -1521134295 + EqualityComparer<Dictionary<string, object>>.Default.GetHashCode(ExtraProperties);
            return hashCode;
        }

        [JsonProperty]
        public Dictionary<string, object> ExtraProperties { get; set; }


    }
}