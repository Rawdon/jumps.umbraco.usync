﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using umbraco.cms.businesslogic;

namespace jumps.umbraco.usync.Models
{
    public static class uDictionaryItem 
    {
        public static XElement SyncExport(this Dictionary.DictionaryItem item)
        {
            XmlDocument xmlDoc = helpers.XmlDoc.CreateDoc();
            xmlDoc.AppendChild(item.ToXml(xmlDoc));
            
            return XElement.Load(new XmlNodeReader(xmlDoc));
        }

        public static ChangeItem SyncImport(XElement node, bool postCheck = true)
        {
            var change = new ChangeItem
            {
                changeType = ChangeType.Success,
                itemType = ItemType.Dictionary,
                name = node.Attribute("Key").Value
            };

            XmlDocument xmlDoc = new XmlDocument();
            xmlDoc.LoadXml(node.ToString());

            var xmlNode = xmlDoc.SelectSingleNode("//DictionaryItem");

            Dictionary.DictionaryItem item = Dictionary.DictionaryItem.Import(xmlNode);
            if (item != null)
            {
                item.Save();
                change.id = item.id;
                change.name = item.key;
            }

            return change; 
        }
    }
}