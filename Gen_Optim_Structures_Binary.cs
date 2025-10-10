using System;
using System.Linq;
using System.Text;
using System.Windows;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;
using Newtonsoft.Json;
using System.IO;
using System.Data;
using static VMS.TPS.Script;
using System.Windows.Forms;
using System.Windows.Documents.DocumentStructures;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TextBox;

#nullable enable

// TODO: Replace the following version attributes by creating AssemblyInfo.cs. You can do this in the properties of the Visual Studio project.
[assembly: AssemblyVersion("1.0.0.10")]
[assembly: AssemblyFileVersion("1.0.0.10")]
[assembly: AssemblyInformationalVersion("1.0")]

// TODO: Uncomment the following line if the script requires write access.
[assembly: ESAPIScript(IsWriteable = true)]

namespace VMS.TPS
{
    public class Script
    {
        public Script()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Execute(ScriptContext context /*,  System.Windows.Window window, ScriptEnvironment environment*/)
        {
            // TODO : Add here the code that is called when the script is launched from Eclipse.
            string pth_Structure_Relation = "./Brain-HA-WBRT-Structures.json";

            if (File.Exists(pth_Structure_Relation))
            {
                System.Windows.MessageBox.Show("structure relations file was found:\n" + pth_Structure_Relation);
            }
            else
            {
                System.Windows.MessageBox.Show("structure relations file was not found");
            }

            // Load the patient and structures from context
            context.Patient.BeginModifications();
            StructureSet plan_structure_set = context.StructureSet;
            List<Structure> structure_list = plan_structure_set.Structures.ToList();

            //Load structure relations from json file
            string jsonString = File.ReadAllText(pth_Structure_Relation);
            //var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            List<Structure_Relation> structure_rels = JsonConvert.DeserializeObject<List<Structure_Relation>>(jsonString);
            if (structure_rels == null)
            {
                throw new ArgumentNullException(
                    "no structure relationships in " + pth_Structure_Relation
                    );
            }
            else
            {
                System.Windows.MessageBox.Show("Loaded " + structure_rels.Count() + " Structure Relations from Json File");
            }
            // sort the structure relationships to have anatomical first, then planning and lastly optimization
            var roleOrder = new Dictionary<string, int>
            {
                {"Anatomical", 0 },
                {"Planning", 1 },
                {"Optimization", 2 },
                {"Helper", 3}
            };
            var sorted_structure_rels = structure_rels.OrderBy(rel => roleOrder[rel.Role]).ToList();
            ShowStructuresInDataGrid(sorted_structure_rels, structure_list);

            // depending on the parent or subtraction structures, we need to set the resolution
            // volume operations are only allowed for structures with the same resolution.
            bool needsHighResolution = false;

            // loop through each relation and create a new structure accordingly
            foreach (Structure_Relation relation in sorted_structure_rels)
            {
                // create a new structure to be added to the structure set
                if (relation.Role == "Anatomical")
                {
                    // make sure the structure is in the plan.
                    List<string> query_structures = new List<string> { relation.Name };
                    List<Structure> structures_found = Get_structures_by_name(structure_list, query_structures);
                    if (structures_found == null)
                    {
                        throw new ArgumentNullException(
                            "no structure " + relation.Name + "was found in" + pth_Structure_Relation
                            );
                    }
                    // System.Windows.MessageBox.Show("found anatomical structure:" + relation.Name);
                }
                else if (relation.Role == "Helper")
                {
                    continue;
                }
                else if (relation.Role == "Planning" || relation.Role == "Optimization")
                {
                    Structure newStructure = plan_structure_set.AddStructure("Organ", relation.Name);
                    if (relation.Parents != null)
                    {
                        List<Structure> parent_structures = Get_structures_by_name(structure_list, relation.Parents);
                        // Check if any of the source structures are high resolution
                        needsHighResolution = parent_structures.Any(s => s.IsHighResolution);
                        if (needsHighResolution)
                        {
                            newStructure.ConvertToHighResolution();
                        }
                        //get parent structure from context; apply union
                        List<Structure> parents_in_plan = Get_structures_by_name(structure_list, relation.Parents);
                        foreach (Structure parent in parents_in_plan)
                        {
                            if (needsHighResolution)
                            {
                                parent.ConvertToHighResolution();
                            }
                            newStructure.SegmentVolume = newStructure.SegmentVolume.Or(parent.SegmentVolume);
                        }
                    }
                    if (relation.Margin != null)
                    {
                        newStructure.SegmentVolume = newStructure.SegmentVolume.Margin(relation.Margin.Value);
                    }
                    // get subtract structures from context; apply subtraction
                    if (relation.Subtract != null)
                    {
                        List<Structure> subtract_structures = Get_structures_by_name(structure_list, relation.Subtract);
                        needsHighResolution = subtract_structures.Any(s => s.IsHighResolution);
                        foreach (Structure subtractStructure in subtract_structures)
                        {
                            if (needsHighResolution)
                            {
                                subtractStructure.ConvertToHighResolution();
                            }
                            newStructure.SegmentVolume = newStructure.SegmentVolume.Sub(subtractStructure.SegmentVolume);
                        }
                    }
                    if (relation.HighResolution != null)
                    {
                        if (relation.HighResolution.Value) { newStructure.ConvertToHighResolution(); }
                    }
                }
            }
        }

        //public void MakeNewStructure()


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
                System.Windows.MessageBox.Show(structure.ToString());
            }

        }
        public class Structure_Relation
        {
            public string? Name { get; set; }
            public string? Role { get; set; }
            public double? Margin { get; set; }
            public List<string>? Parents { get; set; }
            public bool? HighResolution { get; set; }
            public List<string>? Subtract { get; set; }
            public string? Comment { get; set; }
        }

        // Method to show the data grid with your structure info
        void ShowStructuresInDataGrid(List<Structure_Relation> sorted_structure_rels, List<Structure> structure_list)
        {
                // Gather query names and roles
                List<string> allQueryStructures = new List<string>();
                List<string> allRoles = new List<string>();
                foreach (Structure_Relation relation in sorted_structure_rels)
                {
                    allQueryStructures.Add(relation.Name);
                    allRoles.Add(relation.Role);
                }

                // Query all at once
                List<Structure> structures_found = Get_structures_by_name(structure_list, allQueryStructures);
                HashSet<string> foundNames = structures_found.Select(s => s.Id).ToHashSet();

                // Create DataTable to hold info for DataGridView
                DataTable table = new DataTable();
                table.Columns.Add("Role", typeof(string));
                table.Columns.Add("Query Structure Name", typeof(string));
                table.Columns.Add("Found in Plan", typeof(string));

                // Populate the DataTable rows
                for (int i = 0; i < allQueryStructures.Count; i++)
                {
                    bool found = foundNames.Contains(allQueryStructures[i]);
                    table.Rows.Add(allRoles[i], allQueryStructures[i], found.ToString().ToLower());
                }

                // Create and configure a Form to show the DataGridView
                Form form = new Form()
                {
                    Text = "Structure Query Results",
                    Width = 600,
                    Height = 400
                };

                DataGridView dgv = new DataGridView()
                {
                    DataSource = table,
                    Dock = DockStyle.Fill,
                    ReadOnly = true,
                    AllowUserToAddRows = false,
                    AllowUserToDeleteRows = false,
                    AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
                };

                form.Controls.Add(dgv);

                form.ShowDialog();
    }

}
}
