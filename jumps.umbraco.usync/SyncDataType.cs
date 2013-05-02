﻿// #define UMBRACO6

using System;
using System.Collections; 
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Xml;
using umbraco.cms.businesslogic;
using umbraco.cms.businesslogic.datatype ;

using umbraco.BusinessLogic ; 

using System.IO;
using Umbraco.Core.IO;
using umbraco;

//  Check list
// ====================
//  SaveOne         X
//  SaveAll         X
//  OnSave          (Works in 4.11.5)
//  OnDelete        X
//  ReadFromDisk    X

namespace jumps.umbraco.usync
{
    /// <summary>
    /// syncs the data types.
    /// </summary>
    public class SyncDataType
    {
        public static void SaveToDisk(DataTypeDefinition item)
        {
            if (item != null)
            {
                try
                {
                    XmlDocument xmlDoc = helpers.XmlDoc.CreateDoc();
                    xmlDoc.AppendChild(DataTypeToXml(item, xmlDoc));
                    helpers.XmlDoc.SaveXmlDoc(item.GetType().ToString(), item.Text, xmlDoc);
                }
                catch (Exception ex)
                {
                    Log.Add(LogTypes.Error, 0, string.Format("Saving DataType Failed {0} {1}", item.Text, ex.ToString() ));
                }
            }
            else
            {
                Log.Add(LogTypes.Error, 0, "Null DataType Save attempt - aborted");
            }
        }

        public static void SaveAllToDisk()
        {
            try
            {
              
                foreach (DataTypeDefinition item in DataTypeDefinition.GetAll())
                {
                    if (item != null)
                    {
                        SaveToDisk(item);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Add(LogTypes.Error, 0, string.Format("Error saving all DataTypes, {0}", ex.ToString()));
            }
        }

        public static void ReadAllFromDisk()
        {
            string path = IOHelper.MapPath(string.Format("{0}{1}",
                helpers.uSyncIO.RootFolder,
                "DataTypeDefinition"));

            ReadFromDisk(path); 
        }

        public static void ReadFromDisk(string path)
        {
            if (Directory.Exists(path))
            {

                User u = new User(0) ; 

                foreach (string file in Directory.GetFiles(path, "*.config"))
                {
                    XmlDocument xmlDoc = new XmlDocument();
                    xmlDoc.Load(file);
                                      

                    XmlNode node = xmlDoc.SelectSingleNode("//DataType");

                    if (node != null)
                    {
                        DataTypeDefinition d = Import(node, u);
                        if (d != null)
                        {
                            d.Save();
                        }

                        else
                        {
                            Log.Add(LogTypes.Debug, 0, string.Format("NULL NODE FOR {0}", file));
                        }
                    }
                    
                }
            }
        }

        /// <summary>
        /// DataType Import - taken from the core 
        /// 
        /// the core doesn't pass username, so fails on loading
        /// here we just pass usere User(0) - so we can work)
        /// </summary>
        /// <param name="xmlData"></param>
        /// <returns></returns>
        public static DataTypeDefinition Import(XmlNode xmlData, User u)
        {
            if (xmlData != null)
            {
                string _name = xmlData.Attributes["Name"].Value;
                string _id = xmlData.Attributes["Id"].Value;
                string _def = xmlData.Attributes["Definition"].Value;

                DataTypeDefinition dtd;

                if (CMSNode.IsNode(new Guid(_def)))
                {
                    dtd = DataTypeDefinition.GetDataTypeDefinition(new Guid(_def));
                }
                else
                {

                    if (u == null)
                        u = global::umbraco.BusinessLogic.User.GetUser(0);

                    global::umbraco.cms.businesslogic.datatype.controls.Factory f = new global::umbraco.cms.businesslogic.datatype.controls.Factory();

                    dtd = DataTypeDefinition.MakeNew(u, _name, new Guid(_def));
                    var dataType = f.DataType(new Guid(_id));
                    if (dataType == null)
                        throw new NullReferenceException("Could not resolve a data type with id " + _id);



                    dtd.DataType = dataType;
                    dtd.Save();
                }

                //
                // PREVALUES - HELL :: num 4532
                // 
                // Here we are attempting to add new prevalues to a DataType, and remove old ones.
                // between umbraco installs the IDs will change. we are not trying to match them,
                // we are just trying to match, based on value - problem being, if you change 
                // a value's value then this code would think it's a new ID, delete the old one
                // and create a new one - as we are syncing from a dev point of view we are
                // going to do this for now...
                //

                System.Collections.SortedList prevals = PreValues.GetPreValues(dtd.Id);
                Hashtable oldvals = new Hashtable();
                foreach (DictionaryEntry v in prevals)
                {
                    if ((PreValue)v.Value != null)
                    // if (!String.IsNullOrEmpty(((PreValue)v.Value).Value.ToString()))
                    {
                        oldvals.Add(((PreValue)v.Value).Id, ((PreValue)v.Value).Value.ToString());
                    }
                }

                Hashtable newvals = new Hashtable();
                foreach (XmlNode xmlPv in xmlData.SelectNodes("PreValues/PreValue"))
                {
                    XmlAttribute val = xmlPv.Attributes["Value"];

                    if (val != null)
                    {
                        // add new values only - because if we mess with old ones. it all goes pete tong..
                        if ((val.Value != null) && (!oldvals.ContainsValue(val.Value)))
                        {
                            Log.Add(LogTypes.Debug, 0, string.Format("Adding Prevalue [{0}]", val.Value));
                            PreValue p = new PreValue(0, 0, val.Value);
                            p.DataTypeId = dtd.Id;
                            p.Save();
                        }

                        newvals.Add(xmlPv.Attributes["Id"], val.Value);
                    }
                }


                // ok now delete any values that have gone missing between syncs..

                if (!uSyncSettings.Preserve || !uSyncSettings.PreservedPreValueDataTypes.Contains(_id))
                {
                    foreach (DictionaryEntry oldval in oldvals)
                    {
                        if (!newvals.ContainsValue(oldval.Value))
                        {
                            PreValue o = new PreValue((int)oldval.Key);
                            Log.Add(LogTypes.Debug, 0, string.Format("In {0} Deleting prevalue [{1}]", dtd.Text, oldval.Value));
                            o.Delete();
                        }
                    }
                }
                return dtd;
            }
            return null;
        }

        /// <summary>
        /// DataType ToXML - taken from the core (must learn to patch sometime)
        /// 
        /// fixing basic problem, of prevalues not coming out sorted by id (and sort-order)
        /// with thanks to Kenn Jacobsen for info on this. 
        /// </summary>
        /// <param name="dataType">the datatype to export</param>
        /// <param name="xd">the xmldocument</param>
        /// <returns>the xmlelement representation of the type</returns>
        public static XmlElement DataTypeToXml(DataTypeDefinition dataType, XmlDocument xd)
        {
            XmlElement dt = xd.CreateElement("DataType");
            dt.Attributes.Append(xmlHelper.addAttribute(xd, "Name", dataType.Text));
            dt.Attributes.Append(xmlHelper.addAttribute(xd, "Id", dataType.DataType.Id.ToString()));
            dt.Attributes.Append(xmlHelper.addAttribute(xd, "Definition", dataType.UniqueId.ToString()));

            // templates
            XmlElement prevalues = xd.CreateElement("PreValues");
            foreach (PreValue item in GetPreValues(dataType))
            {
                
                XmlElement prevalue = xd.CreateElement("PreValue");
                prevalue.Attributes.Append(xmlHelper.addAttribute(xd, "Id", item.Id.ToString()));
                prevalue.Attributes.Append(xmlHelper.addAttribute(xd, "Value", item.Value));

                prevalues.AppendChild(prevalue);
            }

            dt.AppendChild(prevalues);

            return dt;
        }

        private static List<PreValue> GetPreValues(DataTypeDefinition dataType)
        {
            return PreValues.GetPreValues(dataType.Id).Values.OfType<PreValue>().OrderBy(p => p.SortOrder).ThenBy(p => p.Id).ToList();
        }

        public static void AttachEvents()
        {
            // this only fires in 4.11.5 + 
            DataTypeDefinition.Saving += new DataTypeDefinition.SaveEventHandler(DataTypeDefinition_Saving);

            // but this is 
            DataTypeDefinition.AfterDelete += DataTypeDefinition_AfterDelete;
        }

        public static void DataTypeDefinition_Saving(DataTypeDefinition sender, EventArgs e)
        {
            SaveToDisk((DataTypeDefinition)sender);
        }

#if UMBRACO6
        //
        // umbraco 6.0.4 changed the defintion of this event! 
        //
        public static void DataTypeDefinition_AfterDelete(DataTypeDefinition sender, EventArgs e)
#else 
        public static void DataTypeDefinition_AfterDelete(object sender, DeleteEventArgs e)

#endif 
        {
            if (typeof(DataTypeDefinition) == sender.GetType())
            {
                helpers.XmlDoc.ArchiveFile(sender.GetType().ToString(), ((DataTypeDefinition)sender).Text);
            }

#if UMBRACO6
            // no cancel... 
#else
            e.Cancel = false; 
#endif
            
        }
        
    }
}
