﻿using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Samba.Domain.Models.Entities;
using Samba.Infrastructure.Helpers;
using Samba.Presentation.Common.Widgets;
using Samba.Presentation.Services;
using Samba.Services;

namespace Samba.Modules.EntityModule.Widgets.EntityGrid
{
    [Export(typeof(IWidgetCreator))]
    public class EntityGridWidgetCreator : IWidgetCreator
    {
        private readonly IEntityService _entityService;
        private readonly IUserService _userService;
        private readonly ICacheService _cacheService;

        [ImportingConstructor]
        public EntityGridWidgetCreator(IEntityService entityService, IUserService userService, ICacheService cacheService)
        {
            _entityService = entityService;
            _userService = userService;
            _cacheService = cacheService;
        }

        public string GetCreatorName()
        {
            return "ResourceGrid";
        }

        public string GetCreatorDescription()
        {
            return "Entity Grid";
        }

        public FrameworkElement CreateWidgetControl(IDiagram widget, ContextMenu contextMenu)
        {
            var viewModel = widget as EntityGridWidgetViewModel;
            Debug.Assert(viewModel != null);

            if (widget.DesignMode)
            {
                viewModel.RefreshSync();
                foreach (var entityScreenItemViewModel in viewModel.ResourceSelectorViewModel.EntityScreenItems)
                {
                    entityScreenItemViewModel.IsEnabled = false;
                }
                
            }

            var ret = new EntitySelectorView(viewModel.ResourceSelectorViewModel) { DataContext = viewModel.ResourceSelectorViewModel, ContextMenu = contextMenu, Tag = widget };

            var heightBinding = new Binding("Height") { Source = viewModel, Mode = BindingMode.TwoWay };
            var widthBinding = new Binding("Width") { Source = viewModel, Mode = BindingMode.TwoWay };
            var xBinding = new Binding("X") { Source = viewModel, Mode = BindingMode.TwoWay };
            var yBinding = new Binding("Y") { Source = viewModel, Mode = BindingMode.TwoWay };

            ret.SetBinding(InkCanvas.LeftProperty, xBinding);
            ret.SetBinding(InkCanvas.TopProperty, yBinding);
            ret.SetBinding(FrameworkElement.HeightProperty, heightBinding);
            ret.SetBinding(FrameworkElement.WidthProperty, widthBinding);

            return ret;
        }

        public Widget CreateNewWidget()
        {
            var parameters = JsonHelper.Serialize(new EntityGridWidgetSettings());
            return new Widget { Properties = parameters, CreatorName = GetCreatorName() };
        }

        public IDiagram CreateWidgetViewModel(Widget widget, IApplicationState applicationState)
        {
            return new EntityGridWidgetViewModel(widget, applicationState, _entityService, _userService, _cacheService);
        }
    }
}
