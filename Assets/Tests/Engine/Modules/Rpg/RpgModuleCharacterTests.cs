using BreakInfinity;
using io.github.thisisnozaku.idle.framework.Engine.Modules.Rpg;
using io.github.thisisnozaku.idle.framework.Engine.Modules.Rpg.Configuration;
using io.github.thisisnozaku.idle.framework.Engine.Modules.Rpg.Events;
using MoonSharp.Interpreter;
using NUnit.Framework;
using System;
using System.Collections.Generic;
namespace io.github.thisisnozaku.idle.framework.Tests.Engine.Modules.Rpg
{
    public class RpgModuleCharacterTests : RpgModuleTestsBase
    {
        [Test]
        public void OnUpdateActionMeterIncreasesInCombat()
        {
            random.SetNextValues(0);

            Configure();
            engine.StartEncounter();
            //engine.SetActionPhase("combat");
            engine.GetPlayer<RpgCharacter>().Update(engine, 1);
            Assert.AreEqual(new BigDouble(1), engine.GetPlayer<RpgCharacter>().ActionMeter);
        }

        [Test]
        public void OnUpdateActsWhenActionMeterFull()
        {
            Configure();

            random.SetNextValues(0, 0, 1, 1);

            engine.StartEncounter();
            engine.Watch(CharacterActedEvent.EventName, "test", "globals.triggered = true");
            engine.GetPlayer<RpgCharacter>().Update(engine, (float)((BigDouble)engine.GetProperty("configuration.action_meter_required_to_act")).ToDouble());
            Assert.IsTrue(engine.Scripting.EvaluateStringAsScript("return globals.triggered").Boolean);
        }

        [Test]
        public void CharacterIsDeadWhenCurrentHealthIsZero()
        {
            Configure();

            var player = engine.GetPlayer<RpgCharacter>();
            player.CurrentHealth = 0;
            Assert.IsFalse(player.IsAlive);
        }

        [Test]
        public void ApplyStatusAddsStatusOnCharacter()
        {
            rpgModule.AddStatus(new CharacterStatus.Builder().SetFlag("test").Build(engine, 1));

            Configure();
            engine.GetPlayer<RpgCharacter>().Watch(StatusAddedEvent.EventName, "test", "globals.triggered = true");
            engine.GetPlayer<RpgCharacter>().AddStatus(engine.GetStatuses()[1], new BigDouble(1));

            Assert.AreEqual(true, engine.GetPlayer<RpgCharacter>().GetFlag("test"));
            Assert.IsTrue(engine.Scripting.EvaluateStringAsScript("return globals.triggered").Boolean);
        }

        [Test]
        public void RemoveStatusUndoesStatusEffectOnCharacter()
        {
            var status = new CharacterStatus.Builder().SetFlag("test", true).Build(engine, 1);
            rpgModule.AddStatus(status);
            Configure();

            engine.GetPlayer<RpgCharacter>().Watch(StatusRemovedEvent.EventName, "test", "globals.triggered = true");

            engine.GetPlayer<RpgCharacter>().AddStatus(status, new BigDouble(1));
            engine.GetPlayer<RpgCharacter>().RemoveStatus(status);

            Assert.IsFalse(engine.GetPlayer<RpgCharacter>().GetFlag("test"));
            Assert.IsTrue(engine.Scripting.EvaluateStringAsScript("return globals.triggered").Boolean);
        }

        [Test]
        public void UpdateChangesRemainingDurationOfAppliedStatuses()
        {
            random.SetNextValues(0);
            var status = new CharacterStatus.Builder().SetFlag("test", true).Build(engine, 1);
            rpgModule.AddStatus(status);
            Configure();
            engine.Start();

            engine.StartEncounter();
            engine.GetPlayer<RpgCharacter>().AddStatus(status, new BigDouble(5));

            engine.Update(1);
            Assert.AreEqual(new BigDouble(5), engine.GetPlayer<RpgCharacter>().Statuses[1].InitialTime);
            Assert.AreEqual(new BigDouble(4), engine.GetPlayer<RpgCharacter>().Statuses[1].RemainingTime);
        }

        [Test]
        public void UpdateReducingTimeTo0RemoveStatus()
        {
            random.SetNextValues(0);
            var status = new CharacterStatus.Builder().SetFlag("test", true).Build(engine, 1);
            rpgModule.AddStatus(status);
            Configure();
            
            engine.Start();

            engine.StartEncounter();
            
            engine.GetPlayer<RpgCharacter>().AddStatus(status, new BigDouble(1));
            engine.GetPlayer<RpgCharacter>().Watch(StatusRemovedEvent.EventName, "test", "globals.triggered = true");
            engine.SetActionPhase("combat");

            engine.Update(1);
            Assert.AreEqual(0, engine.GetPlayer<RpgCharacter>().Statuses.Count);
            Assert.IsTrue(engine.Scripting.EvaluateStringAsScript("return globals.triggered").Boolean);
        }

        [Test]
        public void AddItemAddsItemToCharacterIfSlotAvailale()
        {

            Configure();

            var item = new CharacterItem(engine.GetNextAvailableId(), engine, "", new string[] { }, null, null);
            engine.GetPlayer<RpgCharacter>().Watch(ItemAddedEvent.EventName, "test", "globals.triggered = true");
            Assert.IsTrue(engine.GetPlayer<RpgCharacter>().AddItem(item));
        }

        [Test]
        public void AddStatusNotDefinedInEngineThrows()
        {
            Configure();
            var status = new CharacterStatus.Builder().SetFlag("test", true).Build(engine, engine.GetNextAvailableId());
            Assert.Throws<ArgumentNullException>(() =>
            {
                engine.GetPlayer<RpgCharacter>().AddStatus(null, 1);
            });
            Assert.Throws<InvalidOperationException>(() =>
            {
                engine.GetPlayer<RpgCharacter>().AddStatus(status, 1);
            });
        }

        [Test]
        public void AddItemToFullSlotFails()
        {

            Configure();

            var item = new CharacterItem(engine.GetNextAvailableId(), engine, "", new string[] { "head" }, null, null);
            Assert.IsTrue(engine.GetPlayer<RpgCharacter>().AddItem(item));
            Assert.IsFalse(engine.GetPlayer<RpgCharacter>().AddItem(item));
        }

        [Test]
        public void AddItemAppliesItsModifications()
        {
            Configure();

            var item = new CharacterItem(engine.GetNextAvailableId(), engine, "", new string[] { "head" }, new Dictionary<string, Tuple<string, string>>()
            {
                { "Accuracy.multiplier", Tuple.Create("value + 99", "value - 99") }
            }, null);

            var startingAccuracy = engine.GetPlayer<RpgCharacter>().Accuracy.Total;
            engine.GetPlayer<RpgCharacter>().AddItem(item);
            Assert.AreEqual(startingAccuracy * 100, engine.GetPlayer<RpgCharacter>().Accuracy.Total);
            engine.GetPlayer<RpgCharacter>().RemoveItem(item);
            Assert.AreEqual(startingAccuracy, engine.GetPlayer<RpgCharacter>().Accuracy.Total);
        }

        [Test]
        public void AddItemEmitsEvent()
        {
            Configure();

            var item = new CharacterItem(engine.GetNextAvailableId(), engine, "", new string[] { "head" }, new Dictionary<string, Tuple<string, string>>()
            {
                { "Accuracy.multiplier", Tuple.Create("value * 100", "value / 100") }
            }, null);

            engine.GetPlayer<RpgCharacter>().Watch(ItemAddedEvent.EventName, "test", "globals.triggered = true");

            engine.GetPlayer<RpgCharacter>().AddItem(item);
            Assert.IsTrue((bool)engine.GlobalProperties["triggered"]);
        }

        [Test]
        public void RemoveItemEmitsEvent()
        {
            Configure();

            var item = new CharacterItem(engine.GetNextAvailableId(), engine, "", new string[] { "head" }, new Dictionary<string, Tuple<string, string>>()
            {
                { "Accuracy.multiplier", Tuple.Create("value * 100", "value / 100") }
            }, null);

            engine.GetPlayer<RpgCharacter>().Watch(ItemAddedEvent.EventName, "test", "globals.triggered = true");
            engine.GetPlayer<RpgCharacter>().AddItem(item);            
            engine.GetPlayer<RpgCharacter>().RemoveItem(item);
            Assert.IsTrue(engine.Scripting.EvaluateStringAsScript("return globals.triggered").Boolean);
        }

        [Test]
        public void WhenPlayerDiesPlayerActionChangedToResurrecting()
        {
            random.SetNextValues(0);
            Configure();

            engine.GetPlayer<RpgCharacter>().Kill();
            Assert.AreEqual(RpgCharacter.Actions.REINCARNATING, engine.GetPlayer<RpgCharacter>().Action);
        }

        [Test]
        public void WhenPlayerResurrectsStartEncounter()
        {
            random.SetNextValues(0, 0);
            Configure();

            var encounter = engine.StartEncounter();

            engine.Emit(CharacterResurrectedEvent.EventName, new CharacterResurrectedEvent(engine.GetPlayer<RpgCharacter>()));
            Assert.AreNotEqual(engine.GetCurrentEncounter(), encounter);
        }

        [Test]
        public void CharacterResetRemovesStatuses()
        {
            random.SetNextValues(0);
            var status = new CharacterStatus.Builder().SetFlag("test", true).Build(engine, 1);
            rpgModule.AddStatus(status);
            Configure();

            engine.GetPlayer<RpgCharacter>().AddStatus(status, new BigDouble(1));
            engine.GetPlayer<RpgCharacter>().MaximumHealth.BaseValue = 1;
            engine.GetPlayer<RpgCharacter>().Kill();
            engine.GetPlayer<RpgCharacter>().Reset();
            Assert.AreEqual(0, engine.GetPlayer<RpgCharacter>().Statuses.Count);
            Assert.AreEqual(new BigDouble(1), engine.GetPlayer<RpgCharacter>().CurrentHealth);
        }

        [Test]
        public void WhenCreatureDiesPlayerEarnsXpAndGold()
        {
            random.SetNextValues(0, 0);
            Configure();

            var encounter = engine.StartEncounter();

             encounter.Creatures[0].Kill();

            Assert.AreEqual(new BigDouble(10), engine.GetPlayer<RpgCharacter>().Xp);
            Assert.AreEqual(new BigDouble(10), engine.GetPlayer<RpgCharacter>().Gold);
        }

        [Test]
        public void CharactersCanAddAbilities()
        {
            Configure();

            var ability = new CharacterAbility.Builder().ChangeProperty("Accuracy.multiplier", "value * 2", "value / 2").Build( engine, engine.GetNextAvailableId());

            engine.GetPlayer<RpgCharacter>().Watch(AbilityAddedEvent.EventName, "test", "globals.triggered = true");
            engine.GetPlayer<RpgCharacter>().AddAbility(ability);

            Assert.AreEqual(new BigDouble(40), engine.GetPlayer<RpgCharacter>().Accuracy.Total);
            Assert.IsTrue((bool?)engine.GlobalProperties["triggered"]);
        }

        [Test]
        public void CharactersCanRemoveAbilities()
        {
            Configure();

            var ability = new CharacterAbility.Builder().ChangeProperty("Accuracy.multiplier", "value * 2", "value / 2").Build(engine, engine.GetNextAvailableId());

            engine.GetPlayer<RpgCharacter>().AddAbility(ability);
            engine.GetPlayer<RpgCharacter>().RemoveAbility(ability);

            Assert.AreEqual(new BigDouble(20), engine.GetPlayer<RpgCharacter>().Accuracy.Total);
        }

        [Test]
        public void DamageReducesCurrentHealth()
        {
            Configure();

            engine.GetPlayer<RpgCharacter>().CurrentHealth = 10;

            engine.GetPlayer<RpgCharacter>().InflictDamage(5, null);

            Assert.AreEqual(new BigDouble(5), engine.GetPlayer<RpgCharacter>().CurrentHealth);
        }

        [Test]
        public void TakingDamageEmitsEvent()
        {
            Configure();

            engine.GetPlayer<RpgCharacter>().Watch(DamageTakenEvent.EventName, "test", "triggered = true");
            engine.GetPlayer<RpgCharacter>().CurrentHealth = 10;

            engine.GetPlayer<RpgCharacter>().InflictDamage(5, null);

            Assert.AreEqual(new BigDouble(5), engine.GetPlayer<RpgCharacter>().CurrentHealth);
        }

        [Test]
        public void WhenDamageWouldReduceHealthToOrBelowZeroKillTheCharacter()
        {
            random.SetNextValues(0);
            Configure();

            engine.GetPlayer<RpgCharacter>().Watch(CharacterDiedEvent.EventName, "test", "globals.triggered = true");
            engine.GetPlayer<RpgCharacter>().CurrentHealth = 1;

            engine.GetPlayer<RpgCharacter>().InflictDamage(1, null);
            

            Assert.IsTrue((bool)engine.GlobalProperties["triggered"]);
        }

        [Test]
        public void CanConfigureCreatureXpValueCalculationScript()
        {
            random.SetNextValues(0);
            rpgModule.Creatures.XpValueCalculationScript = "return 1";

            Configure();

            var creature = new RpgCharacter(engine, 10);
            engine.Scripting.EvaluateStringAsScript(engine.GetConfiguration<CreaturesConfiguration>("creatures").Initializer, new Dictionary<string, object>() {
                {"creature", creature },
                { "level", 1 },
                { "definition", engine.GetCreatures()[1] }
                });
            creature.Kill();

            Assert.AreEqual(new BigDouble(1), engine.GetPlayer<RpgCharacter>().Xp);
        }

        [Test]
        public void CalculatedCreatureHealth()
        {
            Configure();

            //engine.Logging.ConfigureLogging("creature.generate", UnityEngine.LogType.Log);

            var creature = new RpgCharacter(engine, 10);
            engine.Scripting.EvaluateStringAsScript(engine.GetConfiguration<CreaturesConfiguration>("creatures").Initializer, new Dictionary<string, object>() {
                { "creature", creature },
                { "level", 1 },
                { "definition", engine.GetCreatures()[1] }
                });

            Assert.AreEqual(new BigDouble(10), creature.CurrentHealth);
            Assert.AreEqual(new BigDouble(10), creature.MaximumHealth.Total);
        }

        [Test]
        public void CalculatedPlayerHealth()
        {
            Configure();

            var creature = new RpgCharacter(engine, 10);
            engine.Scripting.EvaluateStringAsScript(engine.GetConfiguration<string>("creatures.Initializer"), new Dictionary<string, object>() {
                {"creature", creature },
                { "level", 1 },
                { "definition", engine.GetCreatures()[1] }
                });

            Assert.AreEqual(new BigDouble(20), engine.GetPlayer<RpgCharacter>().CurrentHealth);
            Assert.AreEqual(new BigDouble(20), engine.GetPlayer<RpgCharacter>().MaximumHealth.Total);
        }

        [Test]
        public void WhenGenerateCreatureReturnsCreatureWithAdditionalAttributes()
        {
            random.SetNextValues(0);

            Configure();

            Assert.AreEqual("bar", engine.GetCreatureDefinitions()[2].Properties["foo"]);
        }

        [Test]
        public void CreatureScaling()
        {
            Configure();
            var baseValue = new BigDouble(10);

            var result = engine.Scripting.EvaluateStringAsScript("return ScaleAttribute(value, level)",
                new Dictionary<string, object>()
                {
                    { "value", baseValue },
                    { "level", 1 }
                }).ToObject<BigDouble>();
            Assert.AreEqual(new BigDouble(10), result);
        }

        [Test]
        public void ErrorInPlayerGenerationScriptThrows()
        {
            rpgModule.Player.ValidationScript = "error('foobar')";

            Assert.Throws(typeof(InvalidOperationException), () =>
            {
                Configure();
            });
        }

        [Test]
        public void DefaultAttributeLevelIncrease()
        {
            Configure();
            Assert.AreEqual(BigDouble.One, engine.GetPlayer<RpgCharacter>().Accuracy.ChangePerLevel);
            Assert.AreEqual(new BigDouble(1), engine.GetPlayer<RpgCharacter>().CriticalHitChance.ChangePerLevel);
            Assert.AreEqual(new BigDouble(1), engine.GetPlayer<RpgCharacter>().CriticalHitDamageMultiplier.ChangePerLevel);
            Assert.AreEqual(new BigDouble(5), engine.GetPlayer<RpgCharacter>().MaximumHealth.ChangePerLevel);
            Assert.AreEqual(BigDouble.One, engine.GetPlayer<RpgCharacter>().Penetration.ChangePerLevel);
            Assert.AreEqual(BigDouble.One, engine.GetPlayer<RpgCharacter>().Precision.ChangePerLevel);
            Assert.AreEqual(BigDouble.One, engine.GetPlayer<RpgCharacter>().Resilience.ChangePerLevel);
            Assert.AreEqual(BigDouble.One, engine.GetPlayer<RpgCharacter>().Defense.ChangePerLevel);
            Assert.AreEqual(BigDouble.One, engine.GetPlayer<RpgCharacter>().Evasion.ChangePerLevel);
        }

        [Test]
        public void IsAliveWhenCurrentHealthAbove0()
        {
            Configure();

            Assert.IsTrue(engine.GetPlayer<RpgCharacter>().CurrentHealth > 0);
            Assert.IsTrue(engine.GetPlayer<RpgCharacter>().IsAlive);

            engine.GetPlayer<RpgCharacter>().CurrentHealth = 0;

            Assert.IsTrue(engine.GetPlayer<RpgCharacter>().CurrentHealth == 0);
            Assert.IsFalse(engine.GetPlayer<RpgCharacter>().IsAlive);
        }

        [Test]
        public void DeserializedEntityCanReplaceAnExisting()
        {
            Configure();

                var serialied = engine.GetSerializedSnapshotString();
                engine = new framework.Engine.IdleEngine();

                Configure();

                engine.DeserializeSnapshotString(serialied);
        }
    }
}