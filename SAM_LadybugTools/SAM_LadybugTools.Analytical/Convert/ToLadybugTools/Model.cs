﻿using HoneybeeSchema;
using SAM.Architectural;
using SAM.Core;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Analytical.LadybugTools
{
    public static partial class Convert
    {
        public static Model ToLadybugTools(this AnalyticalModel analyticalModel, double silverSpacing = Tolerance.MacroDistance, double tolerance = Tolerance.Distance)
        {
            if (analyticalModel == null)
                return null;

            AnalyticalModel analyticalModel_Temp = new AnalyticalModel(analyticalModel);
            
            analyticalModel_Temp.OffsetAperturesOnEdge(0.1, tolerance);
            analyticalModel_Temp.ReplaceTransparentPanels(0.1);

            string uniqueName = Core.LadybugTools.Query.UniqueName(analyticalModel_Temp);

            AdjacencyCluster adjacencyCluster = analyticalModel_Temp.AdjacencyCluster;

            List<Room> rooms = null;
            List<AnyOf<IdealAirSystemAbridged, VAV, PVAV, PSZ, PTAC, ForcedAirFurnace, FCUwithDOAS, WSHPwithDOAS, VRFwithDOAS, FCU, WSHP, VRF, Baseboard, EvaporativeCooler, Residential, WindowAC, GasUnitHeater>> hvacs = null;

            List<Space> spaces = adjacencyCluster?.GetSpaces();
            if (spaces != null)
            {
                hvacs = new List<AnyOf<IdealAirSystemAbridged, VAV, PVAV, PSZ, PTAC, ForcedAirFurnace, FCUwithDOAS, WSHPwithDOAS, VRFwithDOAS, FCU, WSHP, VRF, Baseboard, EvaporativeCooler, Residential, WindowAC, GasUnitHeater>>();
                rooms = new List<Room>();

                Dictionary<double, List<Panel>> dictionary_elevations = Analytical.Query.MinElevationDictionary(adjacencyCluster.GetPanels(), true);
                List<Level> levels = dictionary_elevations?.Keys.ToList().ConvertAll(x => Architectural.Create.Level(x));

                for (int i = 0; i < spaces.Count; i++)
                {
                    Space space = spaces[i];
                    if (space == null)
                        continue;

                    Room room = space.ToLadybugTools(analyticalModel_Temp, silverSpacing, tolerance);
                    if (room == null)
                        continue;

                    if (levels != null && levels.Count > 0)
                    {
                        double elevation_Min = space.MinElevation(adjacencyCluster);
                        if (!double.IsNaN(elevation_Min))
                        {
                            double difference_Min = double.MaxValue;
                            Level level_Min = null;
                            foreach(Level level in levels)
                            {
                                double difference = System.Math.Abs(elevation_Min - level.Elevation);
                                if(difference < difference_Min)
                                {
                                    difference_Min = difference;
                                    level_Min = level;
                                }
                            }

                            room.Story = level_Min.Name;
                        }
                    }

                    IdealAirSystemAbridged idealAirSystemAbridged = new IdealAirSystemAbridged(string.Format("{0}_{1}", room.Identifier, "IdealAir"), string.Format("Ideal Air System Abridged {0}", space.Name));
                    hvacs.Add(idealAirSystemAbridged);

                    if (room.Properties == null)
                        room.Properties = new RoomPropertiesAbridged();

                    if (room.Properties.Energy == null)
                        room.Properties.Energy = new RoomEnergyPropertiesAbridged();

                    room.Properties.Energy.Hvac = idealAirSystemAbridged.Identifier;
                    //room.Properties.Energy.ConstructionSet = "Default Generic Construction Set";
                    //room.Properties.Energy.ProgramType = "Generic Office Program";

                    rooms.Add(room);
                }    
               
            }

            List<Shade> shades = null;
            List<Face> faces_Orphaned = null;

            List<Panel> panels_Shading = adjacencyCluster.GetShadingPanels();
            if(panels_Shading != null)
            {
                foreach (Panel panel_Shading in panels_Shading)
                {
                    if (panels_Shading == null)
                        continue;

                    if (panel_Shading.PanelType == PanelType.Shade)
                    {
                        Shade shade = panel_Shading.ToLadybugTools_Shade();
                        if (shade == null)
                            continue;

                        if (shades == null)
                            shades = new List<Shade>();

                        shades.Add(shade);
                    }
                    else
                    {
                        Face face_Orphaned = panel_Shading.ToLadybugTools_Face();
                        if (face_Orphaned == null)
                            continue;

                        if (faces_Orphaned == null)
                            faces_Orphaned = new List<Face>();

                        faces_Orphaned.Add(face_Orphaned);
                    }
                }
            }

            MaterialLibrary materialLibrary = analyticalModel_Temp?.MaterialLibrary;

            List<Construction> constructions_AdjacencyCluster = adjacencyCluster.GetConstructions();
            List<ApertureConstruction> apertureConstructions_AdjacencyCluster = adjacencyCluster.GetApertureConstructions();

            ConstructionSetAbridged constructionSetAbridged = Query.StandardConstructionSetAbridged("Default Generic Construction Set", TextComparisonType.Equals, true);
            List<AnyOf<ConstructionSetAbridged, ConstructionSet>> constructionSets = new List<AnyOf<ConstructionSetAbridged, ConstructionSet>>();// { constructionSetAbridged  };

            List<AnyOf<OpaqueConstructionAbridged, WindowConstructionAbridged, WindowConstructionShadeAbridged, AirBoundaryConstructionAbridged, OpaqueConstruction, WindowConstruction, WindowConstructionShade, AirBoundaryConstruction, ShadeConstruction>> constructions = new List<AnyOf<OpaqueConstructionAbridged, WindowConstructionAbridged, WindowConstructionShadeAbridged, AirBoundaryConstructionAbridged, OpaqueConstruction, WindowConstruction, WindowConstructionShade, AirBoundaryConstruction, ShadeConstruction>>();
            //HoneybeeSchema.Helper.EnergyLibrary.DefaultConstructions?.ToList().ForEach(x => constructions.Add(x as dynamic));

            Dictionary<string, HoneybeeSchema.Energy.IMaterial> dictionary_Materials = new Dictionary<string, HoneybeeSchema.Energy.IMaterial>();
            if(constructions_AdjacencyCluster != null)
            {
                foreach(Construction construction in constructions_AdjacencyCluster)
                {
                    List<ConstructionLayer> constructionLayers = construction.ConstructionLayers;
                    if (constructionLayers == null)
                        continue;

                    constructions.Add(construction.ToLadybugTools());

                    constructionLayers.Reverse();

                    foreach (ConstructionLayer constructionLayer in constructionLayers)
                    {
                        IMaterial material = constructionLayer.Material(materialLibrary);
                        if (material == null)
                            continue;

                        if (dictionary_Materials.ContainsKey(material.Name))
                            continue;

                        if (material is GasMaterial)
                        {
                            List<Panel> panels = Analytical.Query.Panels(adjacencyCluster, construction);
                            List<double> tilts = panels.ConvertAll(x => Analytical.Query.Tilt(x).Round(Tolerance.MacroDistance));
                            double tilt = tilts.Distinct().ToList().Average();

                            tilt = Units.Convert.ToRadians(tilt); //* (System.Math.PI / 180);

                            dictionary_Materials[material.Name] = ((GasMaterial)material).ToLadybugTools(tilt, constructionLayer.Thickness);
                        }
                        else if(material is OpaqueMaterial)
                        {
                            EnergyMaterial energyMaterial = ((OpaqueMaterial)material).ToLadybugTools();
                            dictionary_Materials[material.Name] = energyMaterial;
                            if (!double.IsNaN(constructionLayer.Thickness))
                                energyMaterial.Thickness = constructionLayer.Thickness;
                        }
                    }


                }
            }

            if(apertureConstructions_AdjacencyCluster != null)
            {
                foreach (ApertureConstruction apertureConstruction in apertureConstructions_AdjacencyCluster)
                {
                    List<ConstructionLayer> constructionLayers = null;

                    constructionLayers = apertureConstruction.PaneConstructionLayers;
                    if (constructionLayers != null)
                    {
                        MaterialType materialType = Analytical.Query.MaterialType(constructionLayers, materialLibrary);
                        if(materialType != MaterialType.Undefined && materialType != MaterialType.Gas)
                        {                           
                            if(materialType == MaterialType.Opaque)
                                constructions.Add(apertureConstruction.ToLadybugTools());
                            else
                                constructions.Add(apertureConstruction.ToLadybugTools_WindowConstructionAbridged());

                            foreach (ConstructionLayer constructionLayer in constructionLayers)
                            {
                                IMaterial material = constructionLayer.Material(materialLibrary);
                                if (material == null)
                                    continue;

                                //string name = Query.PaneMaterialName(material);
                                string name = material.Name;

                                if (dictionary_Materials.ContainsKey(name))
                                    continue;

                                if (material is TransparentMaterial)
                                    dictionary_Materials[name] = ((TransparentMaterial)material).ToLadybugTools();
                                else if (material is GasMaterial)
                                    dictionary_Materials[name] = ((GasMaterial)material).ToLadybugTools_EnergyWindowMaterialGas();
                                else
                                    dictionary_Materials[name] = ((OpaqueMaterial)material).ToLadybugTools();
                            }
                        }
                    }
                }
            }

            Dictionary<System.Guid, ScheduleFixedInterval> dictionary_Schedules = new Dictionary<System.Guid, ScheduleFixedInterval>();
            ProfileLibrary profileLibrary = analyticalModel.ProfileLibrary;

            List<Profile> profiles = profileLibrary?.GetProfiles();
            if (profiles != null)
            {
                foreach (Profile profile in profiles)
                {
                    if (profile == null)
                        continue;

                    if (dictionary_Schedules.ContainsKey(profile.Guid))
                        continue;

                    ScheduleFixedInterval scheduleFixedInterval = profile.ToLadybugTools();
                    if (scheduleFixedInterval == null)
                        continue;

                    //dictionary_Schedules[profile.Guid] = scheduleFixedInterval;
                }
            }

            Dictionary<System.Guid, ProgramType> dictionary_InternalConditions = new Dictionary<System.Guid, ProgramType>();
            if (spaces != null)
            {
                foreach (Space space in spaces)
                {
                    InternalCondition internalCondition = space?.InternalCondition;
                    if (internalCondition == null)
                        continue;

                    if (dictionary_InternalConditions.ContainsKey(internalCondition.Guid))
                        continue;

                    ProgramType programType = space.ToLadybugTools(adjacencyCluster, profileLibrary);
                    if (programType != null)
                        dictionary_InternalConditions[internalCondition.Guid] = programType;
                }
            }

            List<AnyOf<EnergyMaterial, EnergyMaterialNoMass, EnergyWindowMaterialGas, EnergyWindowMaterialGasCustom, EnergyWindowMaterialGasMixture, EnergyWindowMaterialSimpleGlazSys, EnergyWindowMaterialBlind, EnergyWindowMaterialGlazing, EnergyWindowMaterialShade>> materials = new List<AnyOf<EnergyMaterial, EnergyMaterialNoMass, EnergyWindowMaterialGas, EnergyWindowMaterialGasCustom, EnergyWindowMaterialGasMixture, EnergyWindowMaterialSimpleGlazSys, EnergyWindowMaterialBlind, EnergyWindowMaterialGlazing, EnergyWindowMaterialShade>>();
            HoneybeeSchema.Helper.EnergyLibrary.DefaultMaterials?.ToList().ForEach(x => materials.Add(x as dynamic));
            dictionary_Materials.Values.ToList().ForEach(x => materials.Add(x as dynamic));

            List<AnyOf<ScheduleRulesetAbridged, ScheduleFixedIntervalAbridged, ScheduleRuleset, ScheduleFixedInterval>> schedules = new List<AnyOf<ScheduleRulesetAbridged, ScheduleFixedIntervalAbridged, ScheduleRuleset, ScheduleFixedInterval>>();
            HoneybeeSchema.Helper.EnergyLibrary.DefaultScheduleRuleset?.ToList().ForEach(x => schedules.Add(x));
            dictionary_Schedules.Values.ToList().ForEach(x => schedules.Add(x));

            List<AnyOf<ProgramTypeAbridged, ProgramType>> programTypes = new List<AnyOf<ProgramTypeAbridged, ProgramType>>();
            HoneybeeSchema.Helper.EnergyLibrary.DefaultProgramTypes?.ToList().ForEach(x => programTypes.Add(x));
            dictionary_InternalConditions.Values.ToList().ForEach(x => programTypes.Add(x));

            List<ScheduleTypeLimit> scheduleTypeLimits = new List<ScheduleTypeLimit>();
            HoneybeeSchema.Helper.EnergyLibrary.DefaultScheduleTypeLimit?.ToList().ForEach(x => scheduleTypeLimits.Add(x));

            constructionSets.RemoveAll(x => x == null);
            constructions.RemoveAll(x => x == null);
            materials.RemoveAll(x => x == null);

            ModelEnergyProperties modelEnergyProperties = new ModelEnergyProperties(constructionSets, constructions, materials, hvacs, programTypes, schedules, scheduleTypeLimits);

            ModelProperties modelProperties = new ModelProperties(modelEnergyProperties);

            Model model = new Model(uniqueName, modelProperties, adjacencyCluster.Name, null, rooms, faces_Orphaned, shades);
            //model.AngleTolerance = Core.Tolerance.Angle;
            model.AngleTolerance = Units.Convert.ToDegrees(Tolerance.Angle);// 2;
            model.Tolerance = Tolerance.MacroDistance;

            return model;
        }
    }
}