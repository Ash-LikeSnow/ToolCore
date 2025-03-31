using Sandbox.Game.Entities;
using System.Collections.Generic;
using ToolCore.Comp;
using ToolCore.Definitions.Serialised;
using ToolCore.Utils;
using VRage.Game;
using VRageMath;
using static ToolCore.Comp.ToolComp;
using static ToolCore.Utils.Draw;

namespace ToolCore.Session
{
    internal partial class ToolSession
    {
        internal void AvLoop()
        {
            AvComps.ApplyAdditions();
            for (int i = 0; i < AvComps.Count; i++)
            {
                var comp = AvComps[i];
                var tool = comp.ToolEntity;

                //MyAPIGateway.Utilities.ShowNotification($"Running {comp.ActiveEffects.Count} effects", 16);
                //for (int j = comp.ActiveEffects.Count - 1; j >= 0; j--)
                for (int j = 0; j < comp.ActiveEffects.Count; j++)
                {
                    var effects = comp.ActiveEffects[j];

                    if (tool.MarkedForClose)
                    {
                        effects.Expired = true;
                        effects.Dirty = true;
                    }


                    var particlesFinished = !effects.HasParticles || RunParticles(effects, comp.HitInfo);

                    var animationsFinished = !effects.HasAnimations || RunAnimations(effects);

                    if (effects.HasBeams) RunBeams(effects, comp.HitInfo);

                    if (effects.HasSound) RunSound(effects, comp);

                    effects.LastActiveTick = Tick;
                    effects.Restart = false;

                    if (effects.Dirty || effects.Expired && particlesFinished && animationsFinished)
                    {
                        comp.ActiveEffects.RemoveAtFast(j);
                        j--;
                        effects.Clean();
                    }
                }

                if (comp.ActiveEffects.Count == 0)
                {
                    comp.AvActive = false;
                    AvComps.Remove(comp);
                }
            }
            AvComps.ApplyRemovals();
        }

        internal void RunBeams(Effects effects, Hit hit)
        {
            var beams = effects.Beams;
            for (int i = 0; i < beams.Count; i++)
            {
                var beam = beams[i];
                var def = beam.Definition;

                var startPos = Vector3D.Transform(beam.Start.Matrix.Translation, beam.StartParent.PositionComp.WorldMatrixRef);

                Vector3D endPos;
                switch (def.EndLocation)
                {
                    case Location.Emitter:
                        endPos = Vector3D.Transform(beam.End.Matrix.Translation, beam.EndParent.PositionComp.WorldMatrixRef);
                        break;
                    case Location.Hit:
                        if (hit.IsValid)
                        {
                            endPos = hit.Position;
                            break;
                        }
                        var forward = Vector3.Normalize(beam.Start.Matrix.Forward);
                        endPos = Vector3D.Transform(beam.Start.Matrix.Translation + beam.Definition.Length * forward, beam.StartParent.PositionComp.WorldMatrixRef);
                        break;
                    case Location.Forward:
                        forward = Vector3.Normalize(beam.Start.Matrix.Forward);
                        endPos = Vector3D.Transform(beam.Start.Matrix.Translation + beam.Definition.Length * forward, beam.StartParent.PositionComp.WorldMatrixRef);
                        break;
                    default:
                        return;
                }

                DrawLine(startPos, endPos, def.Color, def.Width, def.Material);
            }
        }

        internal bool RunParticles(Effects effects, Hit hit)
        {
            var particles = effects.ParticleEffects;
            //MyAPIGateway.Utilities.ShowNotification($"Running {particles.Count} particles", 16);
            for (int i = 0; i < particles.Count; i++)
            {
                var pEffect = particles[i];
                var def = pEffect.Definition;
                var exists = pEffect.Particle != null;

                if (effects.Expired)
                {
                    if (exists)
                    {
                        pEffect.Particle.Stop(false);
                        pEffect.Particle = null;
                    }
                    continue;
                }

                var create = effects.Restart || effects.LastActiveTick < Tick - 1;
                if (!create && !exists)
                    continue;

                MatrixD matrix;
                Vector3D position;
                var parent = pEffect.Parent;
                switch (def.Location)
                {
                    case Location.Centre:
                        matrix = parent.PositionComp.LocalMatrixRef;
                        position = def.Offset;
                        break;
                    case Location.Emitter:
                        matrix = MatrixD.Normalize(pEffect.Dummy.Matrix);
                        position = matrix.Translation + def.Offset;
                        break;
                    case Location.Hit:
                        matrix = MatrixD.Rescale(parent.PositionComp.LocalMatrixRef, -1);
                        position = Vector3D.Transform(hit.Position, parent.PositionComp.WorldMatrixNormalizedInv);
                        break;
                    default:
                        matrix = MatrixD.Identity;
                        position = Vector3D.Zero;
                        break;
                }
                matrix.Translation = position;

                if (create)
                {
                    if (exists)
                        continue;

                    var renderId = pEffect.Parent.Render.GetRenderObjectID();
                    MyParticleEffect myParticle;

                    string name;
                    if (!def.Lookup || !def.ParticleMap.TryGetValue(hit.Material, out name))
                        name = def.Name;

                    if (!MyParticlesManager.TryCreateParticleEffect(name, ref matrix, ref position, renderId, out myParticle))
                        continue;

                    if (myParticle.Loop)
                    {
                        pEffect.Particle = myParticle;
                    }
                    continue;
                }

                if (exists)
                {
                    pEffect.Particle.WorldMatrix = matrix;
                    //pEffect.Particle.SetTranslation(ref position);
                }

            }
            return true;
        }

        internal bool RunAnimations(Effects effects)
        {
            var animations = effects.Animations;
            var finished = true;
            //MyAPIGateway.Utilities.ShowNotification($"Running {animations.Count} animations", 16);
            for (int i = 0; i < animations.Count; i++)
            {
                var anim = animations[i];
                var subpart = anim.Subpart;
                var def = anim.Definition;

                if (subpart == null)
                {
                    Logs.WriteLine($"Subpart null in animation loop!");
                    continue;
                }

                var closing = anim.Ending || effects.Expired;

                if (closing)
                {
                    if (!def.HasWindup || anim.TransitionState <= 0)
                        continue;

                    anim.Starting = false;

                    finished = false;
                    anim.TransitionState--;
                }

                if (!def.IsContinuous)
                {
                    if (!anim.Ending && anim.RemainingDuration <= 0)
                    {
                        anim.Ending = true;
                        continue;
                    }

                    anim.RemainingDuration--;
                }

                if (def.HasWindup && effects.LastActiveTick < Tick - 1)
                {
                    anim.Starting = true;
                }

                if (anim.Starting)
                {
                    anim.TransitionState++;
                    if (anim.TransitionState >= def.WindupTime - 1)
                        anim.Starting = false;
                }

                var transform = def.Transform;
                var transparency = def.Type == AnimationType.Hide ? 0f : 1f;

                if (anim.Starting || closing)
                {
                    switch (def.Type)
                    {
                        case AnimationType.Rotate:
                            transform = Matrix.CreateFromAxisAngle(def.Direction, anim.TransitionState * def.WindupFraction);
                            break;
                        case AnimationType.Linear:
                            transform = Matrix.CreateTranslation(def.Direction * anim.TransitionState * def.WindupFraction);
                            break;
                        case AnimationType.Hide:
                            transparency = 1f - (anim.TransitionState * def.WindupFraction);
                            break;
                        case AnimationType.Unhide:
                            transparency = anim.TransitionState * def.WindupFraction;
                            break;
                    }
                }

                switch (def.Type)
                {
                    case AnimationType.Rotate:
                        var lm = subpart.PositionComp.LocalMatrixRef;
                        var trans = lm.Translation;
                        Matrix.MultiplyRotation(ref lm, ref transform, out lm);
                        lm.Translation = trans;
                        subpart.PositionComp.SetLocalMatrix(ref lm);
                        break;
                    case AnimationType.Linear:
                        lm = subpart.PositionComp.LocalMatrixRef;
                        lm += transform;
                        subpart.PositionComp.SetLocalMatrix(ref lm);
                        break;
                    case AnimationType.Hide:
                    case AnimationType.Unhide:
                        subpart.Render.Transparency = transparency;
                        break;

                }

            }

            return finished;

        }

        internal void RunSound(Effects effects, ToolComp comp)
        {
            var emitter = comp.SoundEmitter;
            if (emitter == null)
            {
                Logs.WriteLine("Sound emitter null!");
                return;
            }

            if (effects.Expired && !effects.SoundStopped)
            {
                if (emitter.IsPlaying)
                {
                    emitter.StopSound(true);
                    effects.SoundStopped = true;
                    //Logs.WriteLine("Stopping sound (expired)");
                }

                return;
            }

            if (effects.LastActiveTick < Tick - 1)
            {
                if (emitter.IsPlaying)
                {
                    emitter.StopSound(true);
                    //Logs.WriteLine("Stopping sound (overwrite)");
                }

                var sound = effects.SoundDef;

                MySoundPair soundPair;
                if (!sound.Lookup || !sound.SoundMap.TryGetValue(comp.HitInfo.Material, out soundPair))
                    soundPair = sound.SoundPair;

                if (soundPair == null)
                    return;

                emitter.PlaySound(soundPair, force3D: true);
                //Logs.WriteLine($"Playing sound {sound.Name}");
            }

            // ✅ Ensure the emitter updates to check distance and restart if needed
            // This is if a tool block was activate far away from a player it will restart
            // the sound when the player comes within range again.
            emitter.Update();
        }

    }
}
