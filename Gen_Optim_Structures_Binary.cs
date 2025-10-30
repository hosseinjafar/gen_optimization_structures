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
using System.Numerics;
using System.Windows.Media;
using System.Windows.Forms;

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
            //string pth_Structure_Relation = "./Brain-HA-WBRT-Structures.json";

            string pth_Structure_Relation = getRelationsTemplate();

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
            sorted_structure_rels = ParentalSort(sorted_structure_rels);

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
                    try
                    {
                        MakeNewStructure(relation, plan_structure_set);
                    }
                    catch (Exception e) {
                        System.Windows.MessageBox.Show("Failed on structure " + relation.Name);
                    }
                    
                }
            }

            ShowStructuresInDataGrid(sorted_structure_rels, plan_structure_set.Structures.ToList());
        }
        
        /*
         * Purpose:
         *  Opens a navigation window to allow the user to select a 
         *  template structure relations json file.
         */
        public string getRelationsTemplate()
        {
            string templatePath = "";

            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*";
            openFileDialog.Title = "Select Template JSON File";
            openFileDialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                templatePath = openFileDialog.FileName;
                // Use templatePath as your template file
                return templatePath;
            }
            else
            {
                // Handle cancel or no selection
                return "File not found!";
            }
        }

        /*
         * Purpose: To create a new structure based on structure relation from json
         * and add it to the patient's plan structure set.
         * this function assumes that the parents of a new structure already exist. 
         * Inputs:
         * - relation := a structure relation from which a new structure is built.
         * - plan_structure_set := The structure set in the treatment plan.
         * Output:
         * - None := a new structure created based on the parent structures, margine, cutout 
         * structures, and resolution and added to the structure set.
        */
        public void MakeNewStructure(Structure_Relation relation, StructureSet plan_structure_set)
        {
            List<Structure> structure_list = plan_structure_set.Structures.ToList();
            bool structureExists = structure_list.Any(s => s.Name == relation.Name);
            if (structureExists)
            {
                return;
            }
            Structure newStructure = plan_structure_set.AddStructure("Organ", relation.Name);

            // depending on the parent or subtraction structures, we need to set the resolution
            // volume operations are only allowed for structures with the same resolution.
            bool needsHighResolution = false;
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
                    if (needsHighResolution & parent.CanConvertToHighResolution())
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
                    if (needsHighResolution & subtractStructure.CanConvertToHighResolution())
                    {
                        subtractStructure.ConvertToHighResolution();
                    }
                    if (!subtractStructure.IsHighResolution & newStructure.IsHighResolution) {
                        // XXX gotta figure out the resolution business!
                        return;
                    }
                    newStructure.SegmentVolume = newStructure.SegmentVolume.Sub(subtractStructure.SegmentVolume);
                }
            }
            if (relation.HighResolution != null)
            {
                if (relation.HighResolution.Value & newStructure.CanConvertToHighResolution())
                {
                    newStructure.ConvertToHighResolution();
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
            var structureResolutionMap = structures_found.ToDictionary(s => s.Id, s => s.IsHighResolution);

            // Create DataTable to hold info for DataGridView
            DataTable table = new DataTable();
            table.Columns.Add("Role", typeof(string));
            table.Columns.Add("Query Structure Name", typeof(string));
            table.Columns.Add("Found in Plan", typeof(string));
            table.Columns.Add("High Resolution", typeof(string));
            // XXX maybe add volume after

            // Populate the DataTable rows
            for (int i = 0; i < allQueryStructures.Count; i++)
            {
                bool found = foundNames.Contains(allQueryStructures[i]);
                String isHighres_str = "Not Applicable";
                if (found) 
                { 
                    bool isHighres = structureResolutionMap[allQueryStructures[i]];
                    if (isHighres) { isHighres_str = "true"; } else { isHighres_str = "false"; }
                }
                
                table.Rows.Add(
                    allRoles[i],
                    allQueryStructures[i],
                    found.ToString().ToLower(),
                    isHighres_str
                    );
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
            /*
            // CellFormatting event for coloring both columns
            dgv.CellFormatting += (sender, e) =>
            {
                if (e.Value == null) return;
                string text = e.Value.ToString().Trim().ToLower();

                // High Resolution column
                if (dgv.Columns[e.ColumnIndex].Name == "High Resolution")
                {
                    if (text == "true") e.CellStyle.ForeColor = Color.Green;
                    else if (text == "false") e.CellStyle.ForeColor = Color.Red;
                    else if (text == "not applicable") e.CellStyle.ForeColor = Color.Blue;
                }

                // Found in Plan column
                if (dgv.Columns[e.ColumnIndex].Name == "Found in Plan")
                {
                    if (text == "true") e.CellStyle.ForeColor = Color.Green;
                    else if (text == "false") e.CellStyle.ForeColor = Color.Red;
                }
            };
            */
            form.Controls.Add(dgv);

                form.ShowDialog();
        }
        /*
         * Purpose: To sort the structure relations such that parents and subtract 
         * structures always come before the child structure.
         */
        public List<Structure_Relation> ParentalSort(List<Structure_Relation> items)
        {
            var lookup = items.ToDictionary(x => x.Name);

            var visited = new HashSet<string>();
            var visiting = new HashSet<string>(); // Detect cycles
            var sortedList = new List<Structure_Relation>();

            void Dfs(string name)
            {
                if (visited.Contains(name))
                    return;

                if (visiting.Contains(name))
                    throw new InvalidOperationException("Cycle detected in parent-child relationships");

                visiting.Add(name);

                if (lookup.TryGetValue(name, out var item))
                {
                    // Union of Parents and Subtract lists as combined parents
                    var combinedParents = Enumerable.Empty<string>();
                    if (item.Parents != null)
                        combinedParents = combinedParents.Union(item.Parents);
                    if (item.Subtract != null)
                        combinedParents = combinedParents.Union(item.Subtract);

                    foreach (var parentName in combinedParents)
                    {
                        if (!string.IsNullOrWhiteSpace(parentName))
                            Dfs(parentName);
                    }
                }

                visiting.Remove(name);
                visited.Add(name);

                if (lookup.TryGetValue(name, out var node))
                    sortedList.Add(node);
            }

            foreach (var item in items)
            {
                if (!visited.Contains(item.Name))
                    Dfs(item.Name);
            }

            return sortedList;
        }
    }
}
