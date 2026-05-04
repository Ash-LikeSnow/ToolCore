using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Text;
using ToolCore.Definitions;
using ToolCore.Definitions.Serialised;
using ToolCore.Utils;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;

namespace ToolCore.Session
{
    internal partial class ToolSession
    {
        internal void LoadDefinitions()
        {
            foreach (var def in MyDefinitionManager.Static?.GetAllDefinitions())
            {
                if (def == null || !def.Enabled)
                    continue;

                if (def is MyPhysicalMaterialDefinition)
                    LoadPhysicalMaterial(def as MyPhysicalMaterialDefinition);

            }
        }

        internal void LoadPhysicalMaterial(MyPhysicalMaterialDefinition def)
        {
            var start = MyStringId.GetOrCompute("Start");
            var hit = MyStringId.GetOrCompute("Hit");
            Dictionary<MyStringHash, MyPhysicalMaterialDefinition.CollisionProperty> materialProperties;
            if (!def.CollisionProperties.TryGetValue(start, out materialProperties))
                return;

            var pMap = new Dictionary<MyStringHash, string>();
            var sMap = new Dictionary<MyStringHash, MySoundPair>();
            foreach (var material in materialProperties.Keys)
            {
                var cProp = materialProperties[material];
                pMap.Add(material, cProp.ParticleEffect);
                sMap.Add(material, cProp.Sound);
            }
            ParticleMap.Add(def.Id.SubtypeName, pMap);
            SoundMap.Add(def.Id.SubtypeName, sMap);
            Logs.WriteLine($"Added {pMap.Count} material properties for material {def.Id.SubtypeName}");
        }

        internal void LoadSettings(ToolCoreSettings serialisedSettings)
        {
            Settings.Update(serialisedSettings);
            BlockLimits.Update(Session.SessionSettings, serialisedSettings);
            LoadVoxelMaterials();

            foreach (var toolSet in DefinitionMap.Values)
            {
                foreach (var def in toolSet)
                {
                    def.DefineMaterialModifiers(this);
                }
            }

            MaterialCategoryMap.Clear();
        }

        internal void LoadVoxelMaterials()
        {
            foreach (var category in Settings.CategoryModifiers.Keys)
            {
                MaterialCategoryMap.Add(category, new List<MyVoxelMaterialDefinition>());
            }

            foreach (var def in MyDefinitionManager.Static?.GetVoxelMaterialDefinitions())
            {
                if (def == null || !def.Enabled)
                    continue;

                LoadVoxelMaterial(def);
            }
        }

        internal void LoadVoxelMaterial(MyVoxelMaterialDefinition def)
        {
            var materialName = string.IsNullOrEmpty(def.MaterialTypeName) ? "Rock" : def.MaterialTypeName;
            var categories = Settings.CategoryModifiers;

            //var restitution = def.Restitution;
            //if (restitution != 1 && restitution > 0)
            //{
            //    MaterialModifiers.Add(def, restitution);
            //    return;
            //}

            var isOre = !string.IsNullOrEmpty(def.MinedOre) && !def.MinedOre.Equals("Stone") && !def.MinedOre.Equals("Ice");
            float hardness;
            if (isOre && categories.TryGetValue("Ore", out hardness))
            {
                MaterialModifiers.Add(def, hardness);
                MaterialCategoryMap["Ore"].Add(def);
                return;
            }

            foreach (var category in categories.Keys)
            {
                if (!materialName.Contains(category))
                    continue;

                MaterialModifiers.Add(def, categories[category]);
                MaterialCategoryMap[category].Add(def);
                return;
            }

            if (categories.TryGetValue("Rock", out hardness))
            {
                MaterialModifiers.Add(def, hardness);
                MaterialCategoryMap["Rock"].Add(def);

            }
        }

        internal void LoadToolCoreDefs()
        {
            foreach (var mod in MyAPIGateway.Session.Mods)
            {
                if (!MyAPIGateway.Utilities.FileExistsInModLocation(PATH, mod))
                    continue;

                using (var reader = MyAPIGateway.Utilities.ReadFileInModLocation(PATH, mod))
                {
                    while (reader.Peek() != -1)
                    {
                        ImportFile(reader.ReadLine(), mod);
                    }
                }
            }
        }

        private void ImportFile(string name, MyObjectBuilder_Checkpoint.ModItem mod)
        {
            if (name.Length <= 0) return;

            var path = "Data\\" + name;
            if (!MyAPIGateway.Utilities.FileExistsInModLocation(path, mod))
                return;

            Definitions.Serialised.Definitions definitions = null;
            using (var reader = MyAPIGateway.Utilities.ReadFileInModLocation(path, mod))
            {
                StringBuilder builder = new StringBuilder();
                while (reader.Peek() != -1)
                {
                    var line = reader.ReadLine();
                    if (line.Contains("<Definition xsi:type="))
                        line = "<Definition>";
                    else if (line.Contains("<HandItem xsi:type="))
                        line = "<HandItem>";

                    builder.AppendLine(line);

                }

                var data = builder.ToString();
                try
                {
                    definitions = MyAPIGateway.Utilities.SerializeFromXML<Definitions.Serialised.Definitions>(data);

                }
                catch (Exception ex)
                {
                    Logs.LogException(ex);
                }
            }

            if (definitions == null) return;

            ImportDefinitions(definitions.CubeBlocks);
            ImportDefinitions(definitions.HandItems);

        }

        private void ImportDefinitions(Definition[] definitions)
        {
            if (definitions == null || definitions.Length == 0) return;

            var sorterType = new MyObjectBuilderType(typeof(MyObjectBuilder_ConveyorSorter));
            for (int i = 0; i < definitions.Length; i++)
            {
                var definition = definitions[i];
                var values = definition.ToolValues;
                if (values == null || values.Length == 0) continue;

                var toolDefs = new List<ToolDefinition>();
                DefinitionMap[definition.Id] = toolDefs;

                for (int j = 0;  j < values.Length; j++)
                {
                    var toolDef = new ToolDefinition(values[j], this);
                    toolDefs.Add(toolDef);

                    if (definition.Id.TypeId == sorterType)
                        continue;

                    MyDefinitionId Id = definition.Id;
                    var handDef = MyDefinitionManager.Static.TryGetHandItemDefinition(ref Id) as MyEngineerToolBaseDefinition;
                    if (handDef == null)
                        return;

                    if (handDef is MyHandDrillDefinition)
                    {
                        var drill = (MyHandDrillDefinition)handDef;
                        drill.HarvestRatioMultiplier = 0f;
                        drill.DistanceMultiplier = 0f;
                        return;
                    }
                    handDef.DistanceMultiplier = values[j].Length / 2; //Default reach distance is 2m, total formula = 2m * mult * 3
                    handDef.SpeedMultiplier = 0f;
                }

            }
        }

        private void InitPlayers()
        {
            List<IMyPlayer> players = new List<IMyPlayer>();
            MyAPIGateway.Multiplayer.Players.GetPlayers(players);

            for (int i = 0; i < players.Count; i++)
                PlayerConnected(players[i].IdentityId);
        }


    }
}
