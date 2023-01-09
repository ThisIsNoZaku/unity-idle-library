
using MoonSharp.Interpreter;
using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using BreakInfinity;
using io.github.thisisnozaku.idle.framework.Engine.Modules.Rpg.Combat;
using io.github.thisisnozaku.idle.framework.Engine.Modules.Rpg.Events;
using io.github.thisisnozaku.idle.framework.Engine.Modules.Rpg.Configuration;
using io.github.thisisnozaku.idle.framework.Engine.State;

namespace io.github.thisisnozaku.idle.framework.Engine.Modules.Rpg
{
    public class RpgModule : IModule
    {
        private Dictionary<string, object> definitions = new Dictionary<string, object>()
        {
            { "encounters", new Dictionary<long, EncounterDefinition>() },
            { "creatures", new Dictionary<long, CreatureDefinition>() },
            { "statuses", new Dictionary<long, CharacterStatus>() },
            { "abilities", new Dictionary<long, CharacterAbility>() },
            { "items", new Dictionary<long, CharacterItem>() },
        };

        public PlayerConfiguration Player { get; } = new PlayerConfiguration();
        public CreaturesConfiguration Creatures { get; } = new CreaturesConfiguration();

        public Dictionary<string, int> ItemSlots { get; set; } = defaultItemSlots;

        public static readonly Dictionary<string, int> defaultItemSlots = new Dictionary<string, int>()
            {
                {"head", 1 },
                {"neck", 1 },
                {"body", 1 },
                {"back", 1 },
                {"arms", 1 },
                {"hands", 1 },
                {"legs", 1 },
                {"feet", 1 },
                {"fingers", 1 },
            };

        private Dictionary<string, BigDouble> playerDefaultAttributeBonusPerLevel = new Dictionary<string, BigDouble>()
        {
            { RpgCharacter.Attributes.ACCURACY, 1 },
            { RpgCharacter.Attributes.ACTION_SPEED, 1 },
            { RpgCharacter.Attributes.CRITICAL_DAMAGE_MULTIPLIER, 1 },
            { RpgCharacter.Attributes.CRITICAL_HIT_CHANCE, 1 },
            { RpgCharacter.Attributes.DAMAGE, 1 },
            { RpgCharacter.Attributes.DEFENSE, 1 },
            { RpgCharacter.Attributes.EVASION, 1 },
            { RpgCharacter.Attributes.MAXIMUM_HEALTH, 5 },
            { RpgCharacter.Attributes.PENETRATION, 1 },
            { RpgCharacter.Attributes.PRECISION, 1 },
            { RpgCharacter.Attributes.RESILIENCE, 1 },
            { RpgCharacter.Attributes.REGENERATION, .5 },
            { RpgCharacter.Attributes.RESURRECTION_MULTIPLIER, .5 }
        };


        // These are the base values for these attributes for creatures.
        // These values are multiplied by the creature properties and then by the level scaling function.
        private Dictionary<string, BigDouble> creatureBaseAttributes = new Dictionary<string, BigDouble>()
        {
            { RpgCharacter.Attributes.ACCURACY, 10 },
            { RpgCharacter.Attributes.ACTION_SPEED, 5 },
            { RpgCharacter.Attributes.CRITICAL_DAMAGE_MULTIPLIER, 10 },
            { RpgCharacter.Attributes.CRITICAL_HIT_CHANCE, 2 },
            { RpgCharacter.Attributes.DAMAGE, 10 },
            { RpgCharacter.Attributes.DEFENSE, 10 },
            { RpgCharacter.Attributes.EVASION, 10 },
            { RpgCharacter.Attributes.MAXIMUM_HEALTH, 10 },
            { RpgCharacter.Attributes.PENETRATION, 10 },
            { RpgCharacter.Attributes.PRECISION, 10 },
            { RpgCharacter.Attributes.RESILIENCE, 10 }
        };

        public string PostUpdateHook;
        // FIXME: Externalize
        private string defaultAttackHitScript =
            "return {hit=true, description='hit', damageToTarget=math.max(math.floor(attacker.damage.Total - defender.defense.Total), configuration.minimum_attack_damage), attacker=attacker}";
        private string defaultAttackMissScript = "return {hit=false, description='miss', attacker=attacker}";
        private string defaultAttackCriticalHitScript = "return {hit=true, description='critical hit', damageToTarget=math.floor(math.max((attacker.damage.Total - defender.defense.Total), 1) * (1 + (attacker.criticalHitDamageMultiplier.Total / 100))), attacker=attacker}";

        private string defaultCreatureValidator = "if(creature.maximumHealth.Total <= 0) then error('creature health must be at least 1') end";

        public RpgModule()
        {

        }

        public void SetConfiguration(IdleEngine engine)
        {
            UserData.RegisterType<PlayerConfiguration>();
            UserData.RegisterType<CreaturesConfiguration>();
            UserData.RegisterType<RpgCharacter>();
            UserData.RegisterType<RpgEncounter>();
            UserData.RegisterType<CreatureDefinition>();
            UserData.RegisterType<EncounterDefinition>();
            UserData.RegisterType<CharacterStatus>();
            UserData.RegisterType<AttackResultDescription>();
            UserData.RegisterType<CharacterItem>();
            UserData.RegisterType<CharacterAbility>();
            UserData.RegisterType<Tuple<BigDouble, RpgCharacter>>();
            UserData.RegisterExtensionType(typeof(RpgExtensionMethods));
            UserData.RegisterType<NumericAttribute>();
            UserData.RegisterType<KeyValuePair<long, EncounterDefinition>>();

            engine.Scripting.AddTypeAdaptor(new scripting.types.TypeAdapter<IDictionary<long, EncounterDefinition>>.AdapterBuilder<IDictionary<long, EncounterDefinition>>()
                .WithClrConversion(DictionaryTypeAdapter.Converter)
                .Build());


            engine.SetConfiguration("player", Player);
            engine.SetConfiguration("creatures", Creatures);

            engine.SetConfiguration("action_meter_required_to_act", new BigDouble(2));
            engine.SetConfiguration("characterItemSlots", defaultItemSlots);

            engine.SetConfiguration("PlayerAttackScript", Player.AttackScript);

            engine.SetConfiguration("base_tohit", 90);

            engine.SetConfiguration("minimum_attack_damage", 1);
            engine.SetConfiguration("next_encounter_delay", new BigDouble(.5f));

            engine.GlobalProperties["CreatureValidator"] = defaultCreatureValidator;
            engine.GlobalProperties["EncounterSelector"] = Resources.Load<TextAsset>("Lua/Rpg/DefaultEncounterSelectorScript").text;

            engine.Scripting.AddTypeAdaptor<AttackResultDescription>(
                new scripting.types.TypeAdapter<AttackResultDescription>.AdapterBuilder<AttackResultDescription>()
                .WithScriptConversion(DataType.Table, value =>
                {
                    var table = value.Table;
                    bool hit = (bool)table["hit"];
                    string description = (string)table["description"];
                    BigDouble damageToTarget = table.Get("damageToTarget").ToObject<BigDouble>();
                    return new AttackResultDescription(hit, description, damageToTarget, table["attacker"] as RpgCharacter, null, null);
                })
                .Build());

            ConfigureCommands(engine);
        }

        private void ConfigureCommands(IdleEngine engine)
        {
            engine.State.AddHandler(StateMachine.DEFAULT_STATE, "kill", KillCharacter, "kill [id]");
            engine.State.AddHandler(StateMachine.DEFAULT_STATE, "damage", DamageCharacter, "damage [id] [quantity]");
        }

        private void KillCharacter(IdleEngine engine, string[] args)
        {

            long id = CommandArgumentParser.Parse<long>("id", args[1]);
            if (id == 1L)
            {
                engine.GetPlayer<RpgCharacter>().Kill();
            }
            else
            {
                var encounter = engine.GetCurrentEncounter();
                foreach (var character in encounter.Creatures)
                {
                    if (character.Id == id)
                    {
                        character.Kill();
                        return;
                    }
                }
                throw new Exception(String.Format("No character with id {0} found", id));
            }
        }

        private void DamageCharacter(IdleEngine engine, string[] args)
        {
            long id = CommandArgumentParser.Parse<long>("id", args[1]);
            long damage = CommandArgumentParser.Parse<long>("id", args[2]);
            if (id == 1L)
            {
                engine.GetPlayer<RpgCharacter>().InflictDamage(damage, null);
            }
            else
            {
                var encounter = engine.GetCurrentEncounter();
                foreach (var character in encounter.Creatures)
                {
                    if (character.Id == id)
                    {
                        character.InflictDamage(damage, null);
                        return;
                    }
                }
                throw new Exception(String.Format("No character with id {0} found", id));
            }
        }

        public void SetDefinitions(IdleEngine engine)
        {
            foreach (var definitionCategory in definitions)
            {
                engine.GetDefinitions()[definitionCategory.Key] = definitionCategory.Value;
            }
        }

        public void SetGlobalProperties(IdleEngine engine)
        {
            engine.GlobalProperties["CalculateXpValue"] = (Func<RpgCharacter, BigDouble>)(creature =>
            {
                return engine.Scripting.EvaluateStringAsScript(engine.GetConfiguration<string>("creatures.XpValueCalculationScript"), Tuple.Create("creature", (object)creature)).ToObject<BigDouble>();
            });
            engine.GlobalProperties["CalculateGoldValue"] = (Func<RpgCharacter, BigDouble>)(creature =>
            {
                return engine.Scripting.EvaluateStringAsScript(engine.GetConfiguration<string>("creatures.GoldValueCalculationScript"), Tuple.Create("creature", (object)creature)).ToObject<BigDouble>();
            });
            engine.GlobalProperties[RpgModule.Properties.ActionPhase] = "";
            engine.GlobalProperties["stage"] = new BigDouble(1);
            engine.GlobalProperties["OnCreatureDied"] = (Action<RpgCharacter>)(creature =>
            {
                engine.Logging.Log("Character {0} died", "character");
                engine.Scripting.EvaluateStringAsScript(engine.GetConfiguration<string>("player.OnCreatureDiedScript"), Tuple.Create("died", (object)creature)).ToObject<BigDouble>();
                engine.GetCurrentEncounter().IsActive = engine.GetCurrentEncounter().Creatures.Any(c => c.IsAlive);

                bool anyCreaturesAlive = engine.GetCurrentEncounter().Creatures.Any(x => x.IsAlive);
                bool playerAlive = engine.GetPlayer<RpgCharacter>().IsAlive;
                engine.GetCurrentEncounter().IsActive = anyCreaturesAlive && playerAlive;

                if (!engine.GetCurrentEncounter().IsActive)
                {
                    if (!anyCreaturesAlive) {
                        engine.Logging.Log("Every creature is dead so ending encounter", "combat");
                    } else if(!playerAlive)
                    {
                        engine.Logging.Log("Player is dead so ending encounter", "combat");
                    }
                    engine.Emit(EncounterEndedEvent.EventName, (Dictionary<string, object>)null);
                    if (playerAlive)
                    {
                        BigDouble nextEncounterDelay = engine.GetConfiguration<BigDouble>("next_encounter_delay");
                        engine.Logging.Log(string.Format("Because player is alive, scheduling next encounter to start in {0} seconds",
                            nextEncounterDelay));
                        
                        engine.Schedule(nextEncounterDelay.ToDouble(), "engine.StartEncounter()", "Timer to start new encounter.");
                    }
                }
            });

            engine.GlobalProperties["OnCreatureResurrected"] = (Action)(() =>
            {
                engine.StartEncounter();
            });

            engine.Watch(CharacterDiedEvent.EventName, "rpg", "OnCreatureDied(died)");

            engine.Watch(CharacterResurrectedEvent.EventName, "rpg", "OnCreatureResurrected()");

            engine.GlobalProperties["GenerateCreature"] = (Func<CreatureDefinition, BigDouble, RpgCharacter>)((definition, level) =>
            {
                if (level <= 0)
                {
                    throw new ArgumentException("level must be at least 1");
                }
                var creature = new RpgCharacter(engine, engine.GetNextAvailableId());
                engine.Scripting.EvaluateStringAsScript(engine.GetConfiguration<string>("creatures.Initializer"), new Dictionary<string, object>() {
                    {"creature", creature},
                    { "definition", definition },
                    { "level", level }
                }).ToObject<RpgCharacter>();
                return creature;
            });

            engine.GlobalProperties["ScaleCreatureAttribute"] = (Func<BigDouble, BigDouble, BigDouble>)((baseValue, level) =>
            {
                return engine.Scripting.EvaluateStringAsScript(engine.GetConfiguration<string>("creatures.AttributeScalingScript"), new Dictionary<string, object>()
                {
                    { "base", baseValue },
                    { "level", level }
                }).ToObject<BigDouble>();
            });

            engine.GlobalProperties["calculateXpValue"] = (Func<RpgCharacter, BigDouble>)((character) => engine.Scripting.EvaluateStringAsScript(engine.GetConfiguration<string>("xp_calculation_method"), new Dictionary<string, object>()
            {
                { "character", character }
            }).ToObject<BigDouble>());
            engine.GlobalProperties["calculateGoldValue"] = (Func<RpgCharacter, BigDouble>)((character) => engine.Scripting.EvaluateStringAsScript(engine.GetConfiguration<string>("gold_calculation_script"), new Dictionary<string, object>()
            {
                { "character", character }
            }).ToObject<BigDouble>());

            engine.GlobalProperties["AttackResultDescription"] = typeof(AttackResultDescription);

            engine.GlobalProperties["AttackHandlerScripts"] = new Dictionary<string, string>()
            {
                { "hit", defaultAttackHitScript },
                { "miss", defaultAttackMissScript },
                { "critical hit", defaultAttackCriticalHitScript }
            };

            SetDefaultAttributeConfiguration(engine);

            engine.GlobalProperties["startEncounter"] = (Func<EncounterDefinition, RpgEncounter>)engine.StartEncounter;

            engine.GlobalProperties["encounter"] = new RpgEncounter(engine, -1, 1)
            {
                IsActive = false
            };

            engine.GlobalProperties["SelectEncounter"] = (Func<EncounterDefinition>)(() =>
            {
                return engine.Scripting.EvaluateStringAsScript(engine.GlobalProperties["EncounterSelector"] as string).ToObject<EncounterDefinition>();
            });

            engine.GeneratePlayer();
        }

        private void SetDefaultAttributeConfiguration(IdleEngine engine)
        {
            Dictionary<string, object> playerAttributes = (Dictionary<string, object>)(engine.GetConfiguration()["default_player_stats"] = new Dictionary<string, object>());
            Dictionary<string, object> playerAttributePerLevel = (Dictionary<string, object>)(engine.GetConfiguration()["default_player_stat_per_level"] = new Dictionary<string, object>());
            Dictionary<string, object> creatureAttributes = (Dictionary<string, object>)(engine.GetConfiguration()["default_creature_stats"] = new Dictionary<string, object>());
            foreach (var attribute in playerDefaultAttributeBonusPerLevel)
            {
                playerAttributePerLevel[attribute.Key] = attribute.Value;
            }
            foreach (var attribute in creatureBaseAttributes)
            {
                creatureAttributes[attribute.Key] = attribute.Value;
                playerAttributes[attribute.Key] = attribute.Value * 2;
            }
        }

        public void AddCreature(CreatureDefinition creatureDefinition)
        {
            (definitions["creatures"] as Dictionary<long, CreatureDefinition>)[creatureDefinition.Id] = creatureDefinition;
        }

        public void AddAbility(CharacterAbility ability)
        {
            (definitions["abilities"] as Dictionary<long, CharacterAbility>)[ability.Id] = ability;
        }

        public void AddEncounter(EncounterDefinition encounterDefinition)
        {
            foreach (var option in encounterDefinition.CreatureOptions)
            {
                if (!(definitions["creatures"] as Dictionary<long, CreatureDefinition>).ContainsKey(option.Item1))
                {
                    throw new InvalidOperationException("Add a creature with id " + option.Item1 + " first!");
                }
            }
            if (encounterDefinition.CreatureOptions.Length == 0)
            {
                throw new InvalidOperationException("Encounter needs at least one option.");
            }
            (definitions["encounters"] as Dictionary<long, EncounterDefinition>)[encounterDefinition.Id] = encounterDefinition;
        }

        public void AddStatus(CharacterStatus status)
        {
            (definitions["statuses"] as Dictionary<long, CharacterStatus>)[status.Id] = status;
        }

        public void AddItem(CharacterItem item)
        {
            (definitions["items"] as Dictionary<long, CharacterItem>)[item.Id] = item;
        }

        public void AssertReady()
        {
            if ((definitions["encounters"] as Dictionary<long, EncounterDefinition>).Count == 0)
            {
                throw new InvalidOperationException("Need to define at least 1 encounter");
            }
        }

        public static class Properties
        {
            public const string ActionPhase = "action_phase";
        }

        public Dictionary<string, object> GetDefinitions() => definitions;
    }

    public static class RpgExtensionMethods
    {
        public static IDictionary<long, EncounterDefinition> GetEncounterDefinitions(this IdleEngine engine)
        {
            return engine.GetProperty<IDictionary<long, EncounterDefinition>>("definitions.encounters");
        }

        public static Dictionary<long, CharacterStatus> GetStatuses(this IdleEngine engine)
        {
            return (Dictionary<long, CharacterStatus>)engine.GetDefinitions()["statuses"];
        }

        public static Dictionary<long, CreatureDefinition> GetCreatures(this IdleEngine engine)
        {
            return (Dictionary<long, CreatureDefinition>)engine.GetDefinitions()["creatures"];
        }

        public static Dictionary<long, CharacterItem> GetItems(this IdleEngine engine)
        {
            return (Dictionary<long, CharacterItem>)engine.GetDefinitions()["items"];
        }

        public static Dictionary<long, CharacterAbility> GetAbilities(this IdleEngine engine)
        {
            return (Dictionary<long, CharacterAbility>)engine.GetDefinitions()["abilities"];
        }

        public static void SetStage(this IdleEngine engine, BigDouble newStage)
        {
            engine.GlobalProperties["stage"] = newStage;
            engine.Emit(StageChangedEvent.EventName, new StageChangedEvent(newStage));
            engine.StartEncounter();
        }

        public static AttackResultDescription MakeAttack(this IdleEngine engine, RpgCharacter attacker, RpgCharacter defender)
        {
            var context = new Dictionary<string, object>()
            {
                { "attacker", attacker },
                { "defender", defender }
            };
            engine.Logging.Log(LogType.Log, () => String.Format("Character {0} is attacking {1}", attacker.Id, defender.Id), "combat.attack");
            var attackResult = engine.Scripting.EvaluateStringAsScript(attacker.AttackScript, context).String;
            if (attackResult == null)
            {
                throw new InvalidOperationException("Value returned from AttackCalculationScript was not a string!");
            }

            Dictionary<string, string> resultHandlerScripts = engine.GlobalProperties["AttackHandlerScripts"] as Dictionary<string, string>;
            AttackResultDescription attackResultDescription;
            attackResultDescription = engine.Scripting.EvaluateStringAsScript(resultHandlerScripts[attackResult], context).ToObject<AttackResultDescription>();
            engine.Logging.Log(LogType.Log, () => String.Format("Attack result was {0}", attackResultDescription), "combat.attack");
            context["attack"] = attackResultDescription;
            // Call IsAttacking triggers on attacker
            List<string> IsAttackingTriggers;
            if (attacker.OnEventTriggers.TryGetValue("IsAttacking", out IsAttackingTriggers))
            {
                IsAttackingTriggers.Aggregate(attackResultDescription, (result, trigger) => engine.Scripting.EvaluateStringAsScript(trigger, context).ToObject<AttackResultDescription>());
            }
            // Call IsBeingAttacked triggers on defender
            List<string> IsBeingAttackedTriggers;
            if (defender.OnEventTriggers.TryGetValue("IsBeingAttacked", out IsBeingAttackedTriggers))
            {
                IsBeingAttackedTriggers.Aggregate(attackResultDescription, (result, trigger) => engine.Scripting.EvaluateStringAsScript(trigger, context).ToObject<AttackResultDescription>());
            }

            if (attackResultDescription.IsHit)
            {
                attacker.Emit(AttackHitEvent.EventName, new AttackHitEvent(attacker, defender, attackResultDescription));
                defender.Emit(HitByAttackEvent.EventName, new HitByAttackEvent(attacker, defender, attackResultDescription));
            }
            else
            {
                attacker.Emit(AttackMissedEvent.EventName, new AttackMissedEvent(attacker, defender, attackResultDescription));
                defender.Emit(MissedByAttackEvent.EventName, new MissedByAttackEvent(attacker, defender, attackResultDescription));
            }

            if (attackResultDescription.DamageToAttacker.Count > 0)
            {
                engine.Logging.Log(LogType.Log, () => String.Format("Applying #{0} damage effects to {1}", attackResultDescription.DamageToAttacker.Count, attacker.Id), "combat.attack");
                foreach (var damage in attackResultDescription.DamageToAttacker.Where(x => x != null))
                {
                    attacker.InflictDamage(damage.Item1, damage.Item2);
                }
            }
            if (attackResultDescription.DamageToDefender.Count > 0)
            {
                engine.Logging.Log(LogType.Log, () => String.Format("Applying #{0} damage effects to {1}", attackResultDescription.DamageToDefender.Count, defender.Id), "combat.attack");
                foreach (var damage in attackResultDescription.DamageToDefender.Where(x => x != null))
                {
                    defender.InflictDamage(damage.Item1, damage.Item2);
                }
            }

            return attackResultDescription;
        }

        public static RpgEncounter GetCurrentEncounter(this IdleEngine engine)
        {
            return engine.GlobalProperties["encounter"] as RpgEncounter;
        }

        public static void SetActionPhase(this IdleEngine engine, string actionPhase)
        {
            engine.GlobalProperties[RpgModule.Properties.ActionPhase] = actionPhase;
        }

        public static string GetActionPhase(this IdleEngine engine)
        {
            return (string)engine.GlobalProperties[RpgModule.Properties.ActionPhase];
        }

        private static EncounterDefinition GetRandomEncounter(this IdleEngine engine)
        {
            var encounters = engine.GetEncounterDefinitions().Values;
            var options = encounters.ToList();
            int index = (int)engine.RandomInt(options.Count()).ToDouble();
            return options[index];
        }

        /*
         * Start a new encounter.
         * 
         * If an encounter definition is given, use that definition to generate the encounter. Otherwise, use a random one.
         */
        public static RpgEncounter StartEncounter(this IdleEngine engine, EncounterDefinition nextEncounter = null)
        {
            engine.Logging.Log("Starting new encounter", "combat");
            BigDouble stage = engine.GetProperty<BigDouble>("stage");
            RpgEncounter currentEncounter = (RpgEncounter)(engine.GlobalProperties["encounter"] =
                DynValue.FromObject(null, new RpgEncounter(engine, engine.GetNextAvailableId(), stage)
            {
                IsActive = true
            }).ToObject<RpgEncounter>());
            if (nextEncounter == null)
            {
                nextEncounter = engine.GetRandomEncounter();
            }
            foreach (var option in nextEncounter.CreatureOptions)
            {
                // Won't check for if creature exists, a check was made at configuration time
                CreatureDefinition creatureDefinition = engine.GetCreatures()[option.Item1];

                var level = (BigDouble)engine.GlobalProperties["stage"] + option.Item2;
                var creature = engine.Scripting.EvaluateStringAsScript("return GenerateCreature(definition, level)", new Dictionary<string, object>()
                {
                    { "definition", creatureDefinition },
                    { "level", level }
                }).ToObject<RpgCharacter>();
                try
                {
                    engine.Scripting.EvaluateStringAsScript(engine.GetProperty<string>("CreatureValidator"), Tuple.Create<string, object>("creature", creature));
                }
                catch (ScriptRuntimeException ex)
                {
                    throw new InvalidOperationException("Validation failed when generating a creature", ex);
                }
                if (creature == null)
                {
                    throw new InvalidOperationException("GenerateCreature returned null! Ensure your implementation of GenerateCreature returns an object of type RpgCharacter or a subclass.");
                }

                currentEncounter.Creatures.Add(creature);
                currentEncounter.IsActive = true;
            }
            engine.Emit(EncounterStartedEvent.EventName, new EncounterStartedEvent());
            engine.SetActionPhase("combat");
            return currentEncounter;
        }

        public static RpgCharacter GetRandomTarget(this IdleEngine engine, BigDouble friendlyParty)
        {
            engine.Logging.Log("Selecting random target");
            if (friendlyParty == 0)
            {
                var monsters = engine.GetCurrentEncounter().Creatures;
                int index = (int)engine.RandomInt(monsters.Count).ToDouble();
                engine.Logging.Log(string.Format("Selected monster {0}", index), "combat");
                return monsters[index];
            }
            else
            {
                engine.Logging.Log("Selecting player", "combat");
                return engine.GetPlayer<RpgCharacter>();
            }
        }

        public static T GetPlayer<T>(this IdleEngine engine) where T : RpgCharacter
        {
            return engine.GlobalProperties["player"] as T;
        }

        public static RpgCharacter GeneratePlayer(this IdleEngine engine)
        {
            if (engine.GlobalProperties.ContainsKey("player"))
            {
                engine.Logging.Log(LogType.Log, "Player already generated, returning it");
                return engine.GlobalProperties["player"] as RpgCharacter;
            }
            engine.Logging.Log("Generating new instance of player character", "player.character");
            var playerType = engine.GetConfiguration<Type>("player.CharacterType");
            if (!typeof(RpgCharacter).IsAssignableFrom(playerType))
            {
                throw new InvalidOperationException(String.Format("Player.CharacterType must be RpgCharacter or a subclass, but was '{0}'; null means your configuration is wrong.", playerType != null ? playerType.ToString() : "null"));
            }
            var player = Activator.CreateInstance(playerType, engine, 0);
            engine.Scripting.EvaluateStringAsScript(engine.GetConfiguration<string>("player.Initializer"), Tuple.Create<string, object>("player", player)).ToObject<RpgCharacter>();
            try
            {
                engine.Logging.Log("Validating player object");
                engine.Scripting.EvaluateStringAsScript(engine.GetConfiguration<string>("player.ValidationScript"), Tuple.Create<string, object>(
                    "player", player));
            }
            catch (ScriptRuntimeException ex)
            {
                throw new InvalidOperationException("Player failed validation after initialization.", ex);
            }
            engine.GlobalProperties["player"] = player;
            return player as RpgCharacter;
        }

        public static IDictionary<long, CreatureDefinition> GetCreatureDefinitions(this IdleEngine engine)
        {
            return engine.GetDefinitions()["creatures"] as IDictionary<long, CreatureDefinition>;
        }
    }
}