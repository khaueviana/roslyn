﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using Microsoft.CodeAnalysis.Editor.Host;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Notification;
using Microsoft.CodeAnalysis.SymbolMapping;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Text.UI.Commanding;
using Microsoft.VisualStudio.Text.UI.Commanding.Commands;
using Microsoft.VisualStudio.Utilities;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.Editor.Implementation.CallHierarchy
{
    [ExportCommandHandler("CallHierarchy", ContentTypeNames.CSharpContentType, ContentTypeNames.VisualBasicContentType)]
    [Order(After = PredefinedCommandHandlerNames.DocumentationComments)]
    internal class CallHierarchyCommandHandler : ICommandHandler<ViewCallHierarchyCommandArgs>
    {
        private readonly ICallHierarchyPresenter _presenter;
        private readonly CallHierarchyProvider _provider;
        private readonly IWaitIndicator _waitIndicator;

        public bool InterestedInReadOnlyBuffer => true;

        [ImportingConstructor]
        public CallHierarchyCommandHandler([ImportMany] IEnumerable<ICallHierarchyPresenter> presenters, CallHierarchyProvider provider, IWaitIndicator waitIndicator)
        {
            _presenter = presenters.FirstOrDefault();
            _provider = provider;
            _waitIndicator = waitIndicator;
        }

        public bool ExecuteCommand(ViewCallHierarchyCommandArgs args)
        {
            AddRootNode(args);
            return true;
        }

        private void AddRootNode(ViewCallHierarchyCommandArgs args)
        {
            _waitIndicator.Wait(EditorFeaturesResources.Call_Hierarchy, EditorFeaturesResources.Computing_Call_Hierarchy_Information, allowCancel: true, action: waitcontext =>
                {
                    var cancellationToken = waitcontext.CancellationToken;
                    var document = args.SubjectBuffer.CurrentSnapshot.GetOpenDocumentInCurrentContextWithChanges();
                    if (document == null)
                    {
                        return;
                    }

                    var workspace = document.Project.Solution.Workspace;
                    var semanticModel = document.GetSemanticModelAsync(waitcontext.CancellationToken).WaitAndGetResult(cancellationToken);

                    var caretPosition = args.TextView.Caret.Position.BufferPosition.Position;
                    var symbolUnderCaret = SymbolFinder.FindSymbolAtPositionAsync(semanticModel, caretPosition, workspace, cancellationToken)
                        .WaitAndGetResult(cancellationToken);

                    if (symbolUnderCaret != null)
                    {
                        // Map symbols so that Call Hierarchy works from metadata-as-source
                        var mappingService = document.Project.Solution.Workspace.Services.GetService<ISymbolMappingService>();
                        var mapping = mappingService.MapSymbolAsync(document, symbolUnderCaret, waitcontext.CancellationToken).WaitAndGetResult(cancellationToken);

                        if (mapping.Symbol != null)
                        {
                            var node = _provider.CreateItem(mapping.Symbol, mapping.Project, SpecializedCollections.EmptyEnumerable<Location>(), cancellationToken).WaitAndGetResult(cancellationToken);
                            if (node != null)
                            {
                                _presenter.PresentRoot((CallHierarchyItem)node);
                            }
                        }
                    }
                    else
                    {
                        var notificationService = document.Project.Solution.Workspace.Services.GetService<INotificationService>();
                        notificationService.SendNotification(EditorFeaturesResources.Cursor_must_be_on_a_member_name, severity: NotificationSeverity.Information);
                    }
                });
        }

        public CommandState GetCommandState(ViewCallHierarchyCommandArgs args)
        {
            return CommandState.CommandIsAvailable;
        }
    }
}
