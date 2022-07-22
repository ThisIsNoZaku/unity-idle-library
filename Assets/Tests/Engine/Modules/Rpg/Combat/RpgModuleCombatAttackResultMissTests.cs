using BreakInfinity;
using io.github.thisisnozaku.idle.framework.Engine.Modules.Rpg;
using NUnit.Framework;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
namespace io.github.thisisnozaku.idle.framework.Tests.Engine.Modules.Rpg.Combat.Attack
{
    public class RpgModuleCombatAttackResultMissTests : RpgModuleTestsBase
    {
        [Test]
        public void TheDefaultPlayerAttackScriptCanMiss()
        {
            Configure();

            random.SetNextValues(0, 99, 100);

            engine.StartEncounter();

            var result = engine.MakeAttack(engine.GetPlayer<RpgCharacter>(), engine.GetCurrentEncounter().Creatures[0]);

            Assert.AreEqual("miss", result.Description);
        }

        [Test]
        public void AbilitiesOnTheAttackerCanModifyAMissedAttack()
        {
            random.SetNextValues(1, 1, 1, 1, 1);
            rpgModule.AddAbility(new CharacterAbility.Builder()
                .WithEventTrigger("IsAttacking", "attack.isHit = true; attack.description = 'hit'; attack.damageToDefender = 0")
                .Build(engine, 5));

            Configure();

            engine.GetPlayer<RpgCharacter>().AddAbility(engine.GetAbilities()[5]);

            var defender = new RpgCharacter(engine, 7);

            var result = engine.MakeAttack(engine.GetPlayer<RpgCharacter>(), defender);

            Assert.IsTrue(result.IsHit);
            Assert.AreEqual(BigDouble.Zero, result.DamageToDefender);
        }

        [Test]
        public void AbilitiesOnTheDefenderCanModifyAnAttackBeingMade()
        {
            random.SetNextValues(1, 1, 1, 1, 1);
            rpgModule.AddAbility(new CharacterAbility.Builder().WithEventTrigger("IsBeingAttacked", "attack.isHit = true; attack.description = 'miss'; attack.damageToDefender = 10; attack.damageToAttacker = 10")
                .Build(engine, 5));

            Configure();

            var defender = new RpgCharacter(engine, 7);
            defender.AddAbility(engine.GetAbilities()[5]);

            var result = engine.MakeAttack(engine.GetPlayer<RpgCharacter>(), defender);

            Assert.IsTrue(result.IsHit);
            Assert.AreEqual(new BigDouble(10), result.DamageToDefender);
        }
    }
}