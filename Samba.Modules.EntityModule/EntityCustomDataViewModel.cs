﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Samba.Domain.Models.Entities;
using Samba.Infrastructure.Helpers;
using Samba.Presentation.Common;

namespace Samba.Modules.EntityModule
{
    public class CustomDataValueViewModel : ObservableObject
    {
        public CustomDataValueViewModel(CustomDataValue model, Func<EntityCustomField, string, string, bool> action)
        {
            Model = model;
            SetValueAction = action;
        }

        public CustomDataValue Model { get; set; }
        public string Name { get { return Model.Name; } set { Model.Name = value; } }
        public string Value
        {
            get { return Model.Value; }
            set
            {
                if (value != Model.Value)
                {
                    var actionResult = SetValueAction(CustomField, Model.Value, value);
                    if (!actionResult)
                        Model.Value = value;
                    RaisePropertyChanged(() => Value);
                }
            }
        }
        public EntityCustomField CustomField { get { return Model.CustomField; } set { Model.CustomField = value; } }
        public Func<EntityCustomField, string, string, bool> SetValueAction { get; set; }
        public void SetValue(string value)
        {
            Model.Value = value;
            RaisePropertyChanged(() => Value);
        }
    }

    public class EntityCustomDataViewModel : ObservableObject
    {
        public Entity Model { get; set; }
        public EntityType EntityType { get; set; }

        public string GetValue(string name)
        {
            return CustomData.Any(x => x.Name == name)
                ? CustomData.Single(x => x.Name == name).Value
                : string.Empty;
        }

        public EntityCustomDataViewModel(Entity model, EntityType template)
        {
            EntityType = template;
            Model = model;
        }

        public bool IsTextBoxVisible { get { return EntityType != null && string.IsNullOrWhiteSpace(EntityType.PrimaryFieldFormat); } }
        public bool IsMaskedTextBoxVisible { get { return !IsTextBoxVisible; } }

        private ObservableCollection<CustomDataValueViewModel> _customData;
        public ObservableCollection<CustomDataValueViewModel> CustomData
        {
            get { return _customData ?? (_customData = GetCustomData(Model.CustomData)); }
        }

        private ObservableCollection<CustomDataValueViewModel> GetCustomData(string customData)
        {
            var data = new ObservableCollection<CustomDataValueViewModel>();
            try
            {
                if (!string.IsNullOrWhiteSpace(customData))
                    data =
                    new ObservableCollection<CustomDataValueViewModel>(
                        JsonHelper.Deserialize<List<CustomDataValue>>(customData)
                        .Select(x => new CustomDataValueViewModel(x, CustomDataValueUpdatingEvent)));
            }
            finally
            {
                GenerateFields(data);
            }
            return data;
        }

        private void GenerateFields(ICollection<CustomDataValueViewModel> data)
        {
            if (EntityType == null) return;

            data.Where(x => EntityType.EntityCustomFields.All(y => y.Name != x.Name)).ToList().ForEach(x => data.Remove(x));

            foreach (var cf in EntityType.EntityCustomFields)
            {
                var customField = cf;
                var d = data.FirstOrDefault(x => x.Name == customField.Name);
                if (d == null)
                {
                    var customDataValue = new CustomDataValue { Name = cf.Name, CustomField = cf };
                    data.Add(new CustomDataValueViewModel(customDataValue, CustomDataValueUpdatingEvent));
                }
                else d.CustomField = cf;
            }
        }

        bool CustomDataValueUpdatingEvent(EntityCustomField entityCustomField, string oldValue, string newValue)
        {
            var handled = false;
            if (entityCustomField.IsQuery && !string.IsNullOrEmpty(entityCustomField.EditingFormat) && entityCustomField.EditingFormat.Contains('='))
            {
                var value = entityCustomField.Values.FirstOrDefault(x => x.Contains(string.Format("\"{0}\"", newValue)));
                if (value != null)
                {
                    var valueParts = ParseCsv(value);
                    var format = entityCustomField.EditingFormat;

                    for (var i = 0; i < valueParts.Count; i++)
                        format = format.Replace("$" + (i + 1), valueParts[i]);

                    var index = valueParts.Count;
                    while (valueParts.Contains("$") && index < 20)
                    {
                        format = format.Replace("$" + index, "");
                        index++;
                    }

                    format = format.Replace("\r", Environment.NewLine);
                    var formatParts = format.Split(';');

                    foreach (var fieldParts in formatParts.Where(x => x.Contains('=')).Select(formatPart => formatPart.Split(new[] { '=' }, 2)))
                    {
                        var field = CustomData.FirstOrDefault(x => x.Name == fieldParts[0]);
                        if (field == null) continue;
                        field.SetValue(fieldParts[1]);
                        handled = true;
                    }
                }
            }
            return handled;
        }

        private static List<string> ParseCsv(string input)
        {
            var sv = new List<string>();
            var regexObj = new Regex(@"""[^""\r\n]*""|'[^'\r\n]*'|[^,\r\n]*");
            var matchResults = regexObj.Match(input);
            while (matchResults.Success)
            {
                var v = matchResults.Value.Trim(new[] { '"', ' ' });
                if (!string.IsNullOrEmpty(v))
                    sv.Add(v);
                matchResults = matchResults.NextMatch();
            }
            return sv;
        }

        public void Update()
        {
            Model.CustomData = JsonHelper.Serialize(_customData.Select(x => x.Model).ToList());
            RaisePropertyChanged(() => IsMaskedTextBoxVisible);
            RaisePropertyChanged(() => IsTextBoxVisible);
        }
    }
}
