#nullable enable
using System;
using System.Linq;
using System.Text;
using System.Windows;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using System.IO;
using System.Data;

[assembly: AssemblyVersion("1.0.0.1")]

namespace VMS.TPS
{
    public class Script
    {
        public Script()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Execute(ScriptContext context /*, System.Windows.Window window, ScriptEnvironment environment*/)
        {
            string pth_structure_relations = "Brain-HA-WBRT-Structures.json";

            if (File.Exists(pth_structure_relations)) {
                MessageBox.Show("structure relations file was found");
            } else {
                MessageBox.Show("structure relations file was not found");
            }
            // Load the patient and structures from context
            context.Patient.BeginModifications();
            StructureSet plan_structure_set = context.StructureSet;
            List<Structure> structure_list = plan_structure_set.Structures.ToList();

            //Load structure relations from json file
            string jsonString = File.ReadAllText(pth_structure_relations);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var structure_rels = JsonSerializer.Deserialize<List<Structure_Relations>>(jsonString, options);
            if (structure_rels == null)
            {
                throw new ArgumentNullException(
                    "no structure relationships in " + pth_structure_relations
                    );
            }
            // sort the structure relationships to have anatomical first, then planning and lastly optimization
            var roleOrder = new Dictionary<string, int>
            {
                {"Anatomical", 0 },
                {"Planning", 1 },
                {"Optimization", 2 },
            };
            var sorted_structure_rels = structure_rels.OrderBy(rel => roleOrder[rel.Role]).ToList();

            // loop through each relation and create a new structure accordingly
            foreach (Structure_Relations relation in sorted_structure_rels)
            {
                // create a new structure to be added to the structure set
                Structure newStructure = plan_structure_set.AddStructure(relation.Role, relation.Name);
                if (relation.Role == "Anatomical")
                {
                    // make sure the structure is in the plan.
                    List<string> query_structures = new List<string> { relation.Name };
                    List<Structure> structures_found = Get_structures_by_name(structure_list, query_structures);
                    if (structures_found == null)
                    {
                        throw new ArgumentNullException(
                            "no structure " + relation.Name + "was found in" + pth_structure_relations
                            );
                    }
                }
                else if (relation.Role == "Planning" || relation.Role == "Optimization")
                {
                    if (relation.Parents != null)
                    {
                        //get parent structure from context; apply union
                        List<Structure> parents_in_plan = Get_structures_by_name(structure_list, relation.Parents);
                        foreach (Structure parent in parents_in_plan)
                        {
                            newStructure.Or(parent);
                        }
                    }
                    // get subtract structures from context; apply subtraction
                    if (relation.Subtract != null)
                    {
                        List<Structure> subtractions_in_plan = Get_structures_by_name(structure_list, relation.Subtract);
                        foreach (Structure subtraction in subtractions_in_plan)
                        {
                            newStructure.Sub(subtraction);
                        }
                    }
                    if (relation.Margin != null)
                    {
                        newStructure.Margin(relation.Margin.Value);
                    }
                    if (relation.HighResolution != null)
                    {
                        if (relation.HighResolution.Value) { newStructure.ConvertToHighResolution(); }
                    }
                }
            }

        }
        public List<Structure> Get_structures_by_name(List<Structure> structure_list, List<String> name_list)
        {
            HashSet<string> nameSet = new HashSet<string>(name_list);
            var structures_found = structure_list.Where(s => nameSet.Contains(s.Name)).ToList();
            return structures_found;
        }

        public void Print_Structure_Names(List<Structure> structure_list)
        {
            foreach (Structure structure in structure_list)
            {
                MessageBox.Show(structure.ToString());
            }

        }
        public class Structure_Relations
        {
            public string Name { get; set; }
            public string Role { get; set; }
            public double? Margin { get; set; }
            public List<string>? Parents { get; set; }
            public bool? HighResolution { get; set; }
            public List<string>? Subtract { get; set; }
            public string? Comment { get; set; }
        }
    }
}
