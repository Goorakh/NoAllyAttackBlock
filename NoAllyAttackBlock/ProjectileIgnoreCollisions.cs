using NoAllyAttackBlock.Utils;
using RoR2;
using RoR2.Projectile;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace NoAllyAttackBlock
{
    public class ProjectileIgnoreCollisions : MonoBehaviour
    {
        [SystemInitializer(typeof(ProjectileCatalog))]
        static void Init()
        {
            for (int i = 0; i < ProjectileCatalog.projectilePrefabCount; i++)
            {
                GameObject projectilePrefab = ProjectileCatalog.GetProjectilePrefab(i);
                if (projectilePrefab)
                {
                    projectilePrefab.AddComponent<ProjectileIgnoreCollisions>();
                }
            }
        }

        ProjectileController _projectileController;
        ProjectileStickOnImpact _projectileStickOnImpact;

        readonly HashSet<CharacterBody> _ignoringCollisionsWith = [];

        float _ignoringCollisionsCleanTimer;

        void Awake()
        {
            _projectileController = GetComponent<ProjectileController>();
            _projectileStickOnImpact = GetComponent<ProjectileStickOnImpact>();
        }

        void Start()
        {
            updateAllCharacterCollisions();
        }

        void OnEnable()
        {
            _ignoringCollisionsWith.RemoveWhere(b => !b);
            updateAllCharacterCollisions();

            CharacterBody.onBodyStartGlobal += updateIgnoreCollisions;
            TeamComponent.onJoinTeamGlobal += onJoinTeamGlobal;

            NoAllyAttackBlockPlugin.EnablePassThroughForEnemies.SettingChanged += EnablePassThroughForEnemies_SettingChanged;
            NoAllyAttackBlockPlugin.IgnoreStickProjectiles.SettingChanged += IgnoreStickProjectiles_SettingChanged;
            NoAllyAttackBlockPlugin.IgnoreAttackers.OnValueChanged += updateAllCharacterCollisions;
            NoAllyAttackBlockPlugin.IgnoreVictims.OnValueChanged += updateAllCharacterCollisions;
        }

        void OnDisable()
        {
            CharacterBody.onBodyStartGlobal -= updateIgnoreCollisions;
            TeamComponent.onJoinTeamGlobal -= onJoinTeamGlobal;

            NoAllyAttackBlockPlugin.EnablePassThroughForEnemies.SettingChanged -= EnablePassThroughForEnemies_SettingChanged;
            NoAllyAttackBlockPlugin.IgnoreStickProjectiles.SettingChanged -= IgnoreStickProjectiles_SettingChanged;
            NoAllyAttackBlockPlugin.IgnoreAttackers.OnValueChanged -= updateAllCharacterCollisions;
            NoAllyAttackBlockPlugin.IgnoreVictims.OnValueChanged -= updateAllCharacterCollisions;
        }

        void FixedUpdate()
        {
            if (!_projectileController)
                return;

            _ignoringCollisionsCleanTimer -= Time.fixedDeltaTime;
            if (_ignoringCollisionsCleanTimer <= 0f)
            {
                if (_ignoringCollisionsWith.RemoveWhere(b => !b) > 0)
                {
                    _ignoringCollisionsCleanTimer += 10f;
                }
                else
                {
                    _ignoringCollisionsCleanTimer += 30f;
                }
            }
        }

        void EnablePassThroughForEnemies_SettingChanged(object sender, EventArgs e)
        {
            updateAllCharacterCollisions();
        }

        void IgnoreStickProjectiles_SettingChanged(object sender, EventArgs e)
        {
            updateAllCharacterCollisions();
        }

        void onJoinTeamGlobal(TeamComponent teamComponent, TeamIndex newTeam)
        {
            updateIgnoreCollisions(teamComponent.body);
        }

        void updateAllCharacterCollisions()
        {
            foreach (CharacterBody body in CharacterBody.readOnlyInstancesList)
            {
                updateIgnoreCollisions(body);
            }
        }

        void updateIgnoreCollisions(CharacterBody body)
        {
            if (!body || !body.healthComponent || !_projectileController || !_projectileController.owner)
                return;

            // ProjectileController already handles collision with owner
            if (body.gameObject == _projectileController.owner)
                return;

            bool shouldIgnore = NoAllyAttackBlockPlugin.ShouldIgnoreAttackCollision(body.healthComponent, _projectileController.owner);

            if (NoAllyAttackBlockPlugin.IgnoreStickProjectiles.Value && _projectileStickOnImpact && !_projectileStickOnImpact.ignoreCharacters)
            {
                shouldIgnore = false;
            }

            setIgnoreCollisions(body, shouldIgnore);
        }

        void setIgnoreCollisions(CharacterBody body, bool ignore)
        {
            if (!body)
                return;
            
            bool collisionsNeedUpdate = ignore ? _ignoringCollisionsWith.Add(body) : _ignoringCollisionsWith.Remove(body);
            if (collisionsNeedUpdate)
            {
                Collider[] projectileColliders = _projectileController.myColliders;
                if (projectileColliders != null && projectileColliders.Length > 0)
                {
                    Collider[] bodyColliders = body.GetComponentsInChildren<Collider>(true);

                    ModelLocator modelLocator = body.modelLocator;
                    if (modelLocator)
                    {
                        Transform modelTransform = modelLocator.modelTransform;
                        if (modelTransform && !modelTransform.IsChildOf(body.transform))
                        {
                            Collider[] modelColliders = modelTransform.GetComponentsInChildren<Collider>(true);
                            bodyColliders = [.. bodyColliders, .. modelColliders];
                        }
                    }

                    foreach (Collider bodyCollider in bodyColliders)
                    {
                        foreach (Collider projectileCollider in projectileColliders)
                        {
                            Physics.IgnoreCollision(projectileCollider, bodyCollider, ignore);
                        }
                    }
                }

                Log.Debug($"{name} ({_projectileController.netId}) {(ignore ? "disabled" : "enabled")} collision with {Util.GetBestBodyName(body.gameObject)} ({body.netId})");
            }
        }
    }
}
