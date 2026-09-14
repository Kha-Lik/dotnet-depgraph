using System.Text.Json;

using DotNetDependencyGraph.Core.Domain.Graph;
using DotNetDependencyGraph.Core.Application.Filtering;
using DotNetDependencyGraph.Core.Infrastructure.Output;

using Microsoft.Playwright;

using Xunit;

namespace DotNetDependencyGraph.IntegrationTests;

public sealed class ManualViewInteractionTests
{
    [Fact]
    public async Task ManualSelectionIsIndependentAndRespondsToCanvasClicks()
    {
        await using var session = await BrowserSession.CreateAsync();
        var page = session.Page;
        await page.EvaluateAsync("__depgraphDebug.cy.nodes()[0].select()");

        await EnterManualAsync(page);

        Assert.Equal(0, await page.EvaluateAsync<int>("__depgraphDebug.cy.nodes(':selected').length"));
        var node = await NodePointAsync(page, 1);
        await page.Mouse.ClickAsync(node.X, node.Y);
        Assert.Equal(node.Id, await page.EvaluateAsync<string>("__depgraphDebug.cy.nodes(':selected')[0]?.id() || ''"));
        await page.Keyboard.DownAsync("Control");
        await page.Mouse.ClickAsync(node.X, node.Y);
        await page.Keyboard.UpAsync("Control");
        Assert.Equal(0, await page.EvaluateAsync<int>("__depgraphDebug.cy.nodes(':selected').length"));
        await page.Mouse.ClickAsync(node.X, node.Y);
        await page.Mouse.ClickAsync(700, 820);
        Assert.Equal(0, await page.EvaluateAsync<int>("__depgraphDebug.cy.nodes(':selected').length"));
    }

    [Fact]
    public async Task RepeatedCtrlClickTogglesTheClickedNodeWithoutDeselectingOlderNodes()
    {
        await using var session = await BrowserSession.CreateAsync();
        var page = session.Page;
        await EnterManualAsync(page);
        var ids = await page.EvaluateAsync<string[]>("__depgraphDebug.cy.nodes().slice(0, 4).map(node => node.id())");

        var first = await NodePointAsync(page, ids[0]);
        await page.Mouse.ClickAsync(first.X, first.Y);
        await page.Keyboard.DownAsync("Control");
        foreach (var id in ids.Skip(1))
        {
            var point = await NodePointAsync(page, id);
            await page.Mouse.ClickAsync(point.X, point.Y);
        }

        Assert.Equal(ids.Order(), (await SelectedIdsAsync(page)).Order());
        for (var click = 0; click < 3; click++)
        {
            var point = await NodePointAsync(page, ids[3]);
            await page.Mouse.ClickAsync(point.X, point.Y);
            var expected = click % 2 == 0 ? ids[..3] : ids;
            Assert.Equal(expected.Order(), (await SelectedIdsAsync(page)).Order());
        }
        await page.Keyboard.UpAsync("Control");
    }

    [Fact]
    public async Task NodeGrabDoesNotMutateSelectionBeforePointerStateIsCaptured()
    {
        await using var session = await BrowserSession.CreateAsync();
        var page = session.Page;
        await EnterManualAsync(page);
        var ids = await page.EvaluateAsync<string[]>("__depgraphDebug.cy.nodes().slice(0, 4).map(node => node.id())");
        await SelectIdsAsync(page, ids);

        await page.EvaluateAsync("([id]) => { const view=__depgraphDebug.manualView; view.pointerAdditive=false; view.nodeGrab({target:__depgraphDebug.cy.$id(id)}); }", new object[] { ids[3] });

        Assert.Equal(ids.Order(), (await SelectedIdsAsync(page)).Order());
    }

    [Fact]
    public async Task CtrlClickDeselectsClickedNodeWhenSeveralNodesAreSelected()
    {
        await using var session = await BrowserSession.CreateAsync();
        var page = session.Page;
        await EnterManualAsync(page);
        var ids = await page.EvaluateAsync<string[]>("__depgraphDebug.cy.nodes().slice(0, 4).map(node => node.id())");
        var targetId = ids[0];
        var older = ids[1..];
        await SelectIdsAsync(page, ids);
        Assert.Equal(ids.Order(), (await SelectedIdsAsync(page)).Order());
        await page.EvaluateAsync("([id]) => __depgraphDebug.cy.center(__depgraphDebug.cy.$id(id))", new object[] { targetId });
        var point = await NodePointAsync(page, targetId);

        await page.Keyboard.DownAsync("Control");
        await page.Mouse.ClickAsync(point.X, point.Y);
        await page.Keyboard.UpAsync("Control");

        Assert.Equal(older.Order(), (await SelectedIdsAsync(page)).Order());
    }

    [Fact]
    public async Task ExploreRepeatedCtrlClickTogglesTheClickedNodeWithoutDeselectingOlderNodes()
    {
        await using var session = await BrowserSession.CreateAsync();
        var page = session.Page;
        await page.EvaluateAsync("document.getElementById('pause').click()");
        var ids = await page.EvaluateAsync<string[]>("__depgraphDebug.cy.nodes().slice(0, 4).map(node => node.id())");

        var first = await NodePointAsync(page, ids[0]);
        await page.Mouse.ClickAsync(first.X, first.Y);
        await page.Keyboard.DownAsync("Control");
        foreach (var id in ids.Skip(1))
        {
            var point = await NodePointAsync(page, id);
            await page.Mouse.ClickAsync(point.X, point.Y);
        }

        Assert.Equal(ids.Order(), (await SelectedIdsAsync(page)).Order());
        for (var click = 0; click < 3; click++)
        {
            var point = await NodePointAsync(page, ids[3]);
            await page.Mouse.ClickAsync(point.X, point.Y);
            var expected = click % 2 == 0 ? ids[..3] : ids;
            Assert.Equal(expected.Order(), (await SelectedIdsAsync(page)).Order());
        }
        await page.Keyboard.UpAsync("Control");
    }

    [Fact]
    public async Task SharedSvgIconsPreserveAccessibleButtonLabels()
    {
        await using var session = await BrowserSession.CreateAsync();
        var page = session.Page;

        Assert.Equal(1, await page.Locator("#fit > svg.button-icon").CountAsync());
        Assert.Equal("Fit all", await page.Locator("#fit").GetAttributeAsync("aria-label"));
        Assert.Equal(0, await page.Locator("#fit .button-label").CountAsync());

        await EnterManualAsync(page, startUnassigned: true);

        Assert.Equal(1, await page.Locator("#manual-undo > svg.button-icon").CountAsync());
        Assert.Equal("Undo", await page.Locator("#manual-undo").GetAttributeAsync("aria-label"));
        Assert.Equal(0, await page.Locator("#manual-undo .button-label").CountAsync());
        Assert.Equal("Hide connections", await page.Locator("#manual-mute .button-label").TextContentAsync());
        Assert.Equal(1, await page.Locator("#manual-mute > svg.button-icon").CountAsync());
        Assert.Equal("M4 4h10v10H4zM10 10h10v10H10zM12 8v8M8 12h8", await page.Locator("#manual-merge-groups .button-icon path").GetAttributeAsync("d"));
        Assert.Equal(1, await page.Locator(".manual-region-action.svg-icon-button .button-icon").First.CountAsync());
    }

    [Fact]
    public async Task AssignmentDropdownExcludesNoOpTargetsAndPreviewsDestination()
    {
        await using var session = await BrowserSession.CreateAsync();
        var page = session.Page;
        await EnterManualAsync(page, startUnassigned: true);
        var ids = await page.EvaluateAsync<string[]>("__depgraphDebug.cy.nodes().slice(0, 2).map(node => node.id())");
        await CreateGroupAsync(page, "Destination", ids[1]);
        var destination = await page.EvaluateAsync<string>("([id]) => __depgraphDebug.manualView.model.board.placements[id].groupId", new object[] { ids[1] });

        await SelectIdsAsync(page, ids[0]);

        Assert.False(await page.Locator("#manual-assign-target").IsDisabledAsync());
        Assert.Equal("Move selected to…", await page.Locator("#manual-assign-target option").First.TextContentAsync());
        Assert.Equal(0, await page.Locator("#manual-assign-target option[value='group:unassigned']").CountAsync());
        await page.Locator("#manual-assign-target").SelectOptionAsync(destination);
        Assert.True(await page.Locator($".manual-region[data-group-id='{destination}']").EvaluateAsync<bool>("region => region.classList.contains('assignment-target')"));
        Assert.Equal("Move 1 selected", await page.Locator("#manual-assign .button-label").TextContentAsync());
        Assert.False(await page.Locator("#manual-assign").IsDisabledAsync());

        await page.Locator("#manual-assign").ClickAsync();

        Assert.Equal(destination, await page.EvaluateAsync<string>("([id]) => __depgraphDebug.manualView.model.board.placements[id].groupId", new object[] { ids[0] }));
        Assert.False(await page.Locator("#manual-assign").IsEnabledAsync());
        Assert.Empty(await BoardErrorsAsync(page));
    }

    [Fact]
    public async Task MovingManyNodesAutomaticallyEnlargesTheTargetGroup()
    {
        await using var session = await BrowserSession.CreateAsync();
        var page = session.Page;
        await EnterManualAsync(page, startUnassigned: true);
        var ids = await page.EvaluateAsync<string[]>("__depgraphDebug.cy.nodes().map(node => node.id())");
        await CreateGroupAsync(page, "Small destination", ids[0]);
        var target = await page.EvaluateAsync<string>("([id]) => __depgraphDebug.manualView.model.board.placements[id].groupId", new object[] { ids[0] });
        var before = await GroupGeometryAsync(page, target);
        await SelectIdsAsync(page, ids.Skip(1).ToArray());

        await page.Locator("#manual-assign-target").SelectOptionAsync(target);
        await page.Locator("#manual-assign").ClickAsync();

        var after = await GroupGeometryAsync(page, target);
        Assert.True(after.Width > before.Width || after.Height > before.Height);
        Assert.True(await page.EvaluateAsync<bool>("([ids, target]) => ids.every(id => __depgraphDebug.manualView.model.board.placements[id].groupId === target)", new object[] { ids, target }));
        Assert.False(await page.Locator("#warning").IsVisibleAsync());
        Assert.Empty(await BoardErrorsAsync(page));
    }

    [Fact]
    public async Task ManualLayoutPaintsIntermediatePositionsWhileRunning()
    {
        await using var session = await BrowserSession.CreateAsync();
        var page = session.Page;
        await EnterManualAsync(page, startUnassigned: true);
        await page.EvaluateAsync("window.__manualBefore = Object.fromEntries(__depgraphDebug.cy.nodes().map(node => [node.id(), {...node.position()}]))");

        await page.Locator("#manual-run").ClickAsync();

        await page.WaitForFunctionAsync("() => __depgraphDebug.manualView.layout.running.size > 0 && __depgraphDebug.cy.nodes().some(node => { const before=window.__manualBefore[node.id()], board=__depgraphDebug.manualView.model.board.placements[node.id()], shown=node.position(); return Math.hypot(board.x-before.x,board.y-before.y) > 0.1 && Math.hypot(shown.x-board.x,shown.y-board.y) < 0.001; })", null, new() { Timeout = 10_000 });
        Assert.Equal("Layout running", await page.Locator("#manual-run-status").TextContentAsync());
        await page.Locator("#manual-pause").ClickAsync();
        Assert.Equal("Resume layout", await page.Locator("#manual-pause").TextContentAsync());
    }

    [Fact]
    public async Task SelectedManualNodeHighlightsItsDependencyEdges()
    {
        await using var session = await BrowserSession.CreateAsync();
        var page = session.Page;
        await EnterManualAsync(page, startUnassigned: true);
        var nodeId = await page.EvaluateAsync<string>("__depgraphDebug.cy.edges()[0].source().id()");
        var node = await NodePointAsync(page, nodeId);

        await page.Mouse.ClickAsync(node.X, node.Y);

        Assert.Equal(nodeId, await page.EvaluateAsync<string>("__depgraphDebug.cy.nodes(':selected')[0].id()"));
        Assert.True(await page.EvaluateAsync<bool>("([id]) => { const edges=__depgraphDebug.cy.$id(id).connectedEdges(); return edges.length > 0 && edges.every(edge => edge.hasClass('upstream') || edge.hasClass('downstream')); }", new object[] { nodeId }));
        Assert.True(await page.EvaluateAsync<bool>("__depgraphDebug.cy.elements('.faded').length > 0"));

        await page.EvaluateAsync("__depgraphDebug.cy.nodes().unselect()");

        Assert.Equal(0, await page.EvaluateAsync<int>("__depgraphDebug.cy.elements('.upstream,.downstream,.faded').length"));
    }

    [Fact]
    public async Task GroupHeaderDragAndResizeHandleCommitGeometry()
    {
        await using var session = await BrowserSession.CreateAsync();
        var page = session.Page;
        await EnterManualAsync(page);
        var groupId = await page.EvaluateAsync<string>("Object.keys(__depgraphDebug.manualView.model.board.groups)[0]");
        var before = await GroupGeometryAsync(page, groupId);
        var zoom = await page.EvaluateAsync<double>("__depgraphDebug.cy.zoom()");

        await DragAsync(page, page.Locator($".manual-region[data-group-id='{groupId}'] .manual-region-header"), 85, 55);
        var moved = await GroupGeometryAsync(page, groupId);
        Assert.InRange(moved.Left - before.Left, 80 / zoom, 90 / zoom);
        Assert.InRange(moved.Top - before.Top, 50 / zoom, 60 / zoom);

        await DragAsync(page, page.Locator($".manual-region[data-group-id='{groupId}'] .manual-resize-handle"), 75, 65);
        var resized = await GroupGeometryAsync(page, groupId);
        Assert.True(resized.Width > moved.Width + 65 / zoom, $"Width did not grow: {moved.Width} -> {resized.Width}");
        Assert.True(resized.Height > moved.Height + 55 / zoom, $"Height did not grow: {moved.Height} -> {resized.Height}");
        await DragAsync(page, page.Locator($".manual-region[data-group-id='{groupId}'] .manual-resize-handle"), -80, -70);
        var shrunk = await GroupGeometryAsync(page, groupId);
        Assert.True(shrunk.Width < resized.Width); Assert.True(shrunk.Height < resized.Height);

        var body = await GroupBodyPointAsync(page, groupId);
        var boxSelectionBefore = await page.EvaluateAsync<bool>("__depgraphDebug.cy.boxSelectionEnabled()");
        await page.Mouse.MoveAsync(body.X, body.Y); await page.Mouse.DownAsync(); await page.Mouse.MoveAsync(body.X + 65, body.Y + 40, new() { Steps = 8 });
        Assert.False(await page.EvaluateAsync<bool>("__depgraphDebug.cy.boxSelectionEnabled()"));
        await page.Mouse.UpAsync();
        Assert.Equal(boxSelectionBefore, await page.EvaluateAsync<bool>("__depgraphDebug.cy.boxSelectionEnabled()"));
        var bodyMoved = await GroupGeometryAsync(page, groupId);
        Assert.InRange(bodyMoved.Left - shrunk.Left, 60 / zoom, 70 / zoom);
        Assert.InRange(bodyMoved.Top - shrunk.Top, 35 / zoom, 45 / zoom);
        Assert.Empty(await BoardErrorsAsync(page));
    }

    [Fact]
    public async Task GroupHeaderClicksSelectAndToggleGroups()
    {
        await using var session = await BrowserSession.CreateAsync();
        var page = session.Page;
        await EnterManualAsync(page, startUnassigned: true);
        var ids = await page.EvaluateAsync<string[]>("__depgraphDebug.cy.nodes().slice(0, 2).map(node => node.id())");
        await CreateGroupAsync(page, "Feature One", ids[0]);
        await CreateGroupAsync(page, "Feature Two", ids[1]);
        var groupIds = await page.EvaluateAsync<string[]>("Object.keys(__depgraphDebug.manualView.model.board.groups).filter(id => id !== 'group:unassigned')");
        await page.EvaluateAsync("__depgraphDebug.manualView.fitBoard(__depgraphDebug.manualView.model.board)");
        await page.WaitForTimeoutAsync(50);

        await page.Locator($".manual-region[data-group-id='{groupIds[0]}'] .manual-region-header").ClickAsync(new() { Position = new() { X = 12, Y = 12 } });
        Assert.Equal(new[] { groupIds[0] }, await page.EvaluateAsync<string[]>("[...__depgraphDebug.manualView.selectedGroupIds]"));

        await page.Keyboard.DownAsync("Control");
        await page.Locator($".manual-region[data-group-id='{groupIds[1]}'] .manual-region-header").ClickAsync(new() { Position = new() { X = 12, Y = 12 } });
        await page.Keyboard.UpAsync("Control");
        Assert.Equal(groupIds.Order(), (await page.EvaluateAsync<string[]>("[...__depgraphDebug.manualView.selectedGroupIds]")).Order());

        await page.Keyboard.DownAsync("Control");
        await page.Locator($".manual-region[data-group-id='{groupIds[1]}'] .manual-region-header").ClickAsync(new() { Position = new() { X = 12, Y = 12 } });
        await page.Keyboard.UpAsync("Control");
        Assert.Equal(new[] { groupIds[0] }, await page.EvaluateAsync<string[]>("[...__depgraphDebug.manualView.selectedGroupIds]"));
    }

    [Fact]
    public async Task ShapeChangeRepacksMembersIntoAValidCircle()
    {
        await using var session = await BrowserSession.CreateAsync();
        var page = session.Page;
        await EnterManualAsync(page, startUnassigned: true);
        await page.Locator("#manual-group-list button").First.ClickAsync();
        await page.Locator("#manual-edit-shape").SelectOptionAsync("circle");
        await page.Locator("#manual-update-group").ClickAsync();

        Assert.Equal("circle", await page.EvaluateAsync<string>("__depgraphDebug.manualView.model.board.groups['group:unassigned'].shape"));
        Assert.Equal(1, await page.Locator(".manual-region[data-group-id='group:unassigned'] .manual-resize-handle .button-icon").CountAsync());
        Assert.True(await page.EvaluateAsync<bool>("() => { const group=__depgraphDebug.manualView.model.board.groups['group:unassigned'], handle=document.querySelector(\".manual-region[data-group-id='group:unassigned'] .manual-resize-handle\"), matrix=handle.transform.baseVal.consolidate().matrix, center={x:matrix.e+9,y:matrix.f+9}; return Math.abs(Math.hypot(center.x-group.cx,center.y-group.cy)-group.radius) < 0.01; }"));
        Assert.Empty(await BoardErrorsAsync(page));
    }

    [Fact]
    public async Task NodeDragPinMuteUndoAndRedoUseManualTransactions()
    {
        await using var session = await BrowserSession.CreateAsync();
        var page = session.Page;
        await EnterManualAsync(page, startUnassigned: true);
        var node = await NodePointAsync(page, 0);
        var before = await page.EvaluateAsync<Position>("([id]) => __depgraphDebug.manualView.model.board.placements[id]", new object[] { node.Id });

        await page.Mouse.MoveAsync(node.X, node.Y); await page.Mouse.DownAsync(); await page.Mouse.MoveAsync(node.X + 45, node.Y + 30, new() { Steps = 6 }); await page.Mouse.UpAsync();
        var moved = await page.EvaluateAsync<Position>("([id]) => __depgraphDebug.manualView.model.board.placements[id]", new object[] { node.Id });
        Assert.True(Math.Abs(moved.X - before.X) > 10 || Math.Abs(moved.Y - before.Y) > 10);
        Assert.Empty(await BoardErrorsAsync(page));

        await page.Locator("#manual-pin").ClickAsync();
        await page.Locator("#manual-mute").ClickAsync();
        Assert.True(await page.EvaluateAsync<bool>("([id]) => __depgraphDebug.manualView.model.board.placements[id].pinned", new object[] { node.Id }));
        Assert.Contains(node.Id, await page.EvaluateAsync<string[]>("__depgraphDebug.manualView.model.board.mutedEntityIds"));
        await page.Locator("#manual-undo").ClickAsync();
        Assert.DoesNotContain(node.Id, await page.EvaluateAsync<string[]>("__depgraphDebug.manualView.model.board.mutedEntityIds"));
        await page.Locator("#manual-redo").ClickAsync();
        Assert.Contains(node.Id, await page.EvaluateAsync<string[]>("__depgraphDebug.manualView.model.board.mutedEntityIds"));
    }

    [Fact]
    public async Task HoverShowsMutedNodeLabelWhileAnotherNodeIsSelected()
    {
        await using var session = await BrowserSession.CreateAsync();
        var page = session.Page;
        await EnterManualAsync(page);
        var ids = await page.EvaluateAsync<string[]>("__depgraphDebug.cy.nodes().slice(0, 2).map(node => node.id())");
        await page.EvaluateAsync("([ids]) => { const [selectedId, mutedId]=ids, view=__depgraphDebug.manualView, muted=__depgraphDebug.cy.$id(mutedId); view.model.board.mutedEntityIds=[mutedId]; view.paint(); view.applyNodeSelection([selectedId]); muted.addClass('faded'); __depgraphDebug.cy.center(muted); }", new object[] { ids });
        Assert.True(await page.EvaluateAsync<bool>("([id]) => __depgraphDebug.cy.$id(id).hasClass('manual-muted') && __depgraphDebug.cy.$id(id).hasClass('faded')", new object[] { ids[1] }));

        await page.EvaluateAsync("([id]) => __depgraphDebug.cy.$id(id).emit('mouseover')", new object[] { ids[1] });

        Assert.Equal(ids[1], await page.EvaluateAsync<string>("__depgraphDebug.state.hovered"));
        Assert.True(await page.EvaluateAsync<bool>("([id]) => { const node=__depgraphDebug.cy.$id(id); return node.hasClass('hovered') && node.hasClass('show-label') && node.style('label') === node.data('label') && Number(node.style('opacity')) === 1; }", new object[] { ids[1] }));
    }

    [Fact]
    public async Task ManualSearchHighlightsNameMatchesAfterThreeCharactersAndPersists()
    {
        await using var session = await BrowserSession.CreateAsync();
        var page = session.Page;
        await EnterManualAsync(page, startUnassigned: true);
        var label = await page.EvaluateAsync<string>("__depgraphDebug.cy.nodes().map(node => node.data('label')).find(label => label.length >= 4)");
        var query = label[..3].ToLowerInvariant();
        var search = page.Locator("#manual-search");
        Assert.True(await search.EvaluateAsync<bool>("input => input.parentElement.id === 'manual-groups'"));

        await search.FillAsync(query[..2]);
        Assert.Equal(0, await page.EvaluateAsync<int>("__depgraphDebug.cy.nodes('.manual-search-match').length"));
        await search.FillAsync(query);
        var matches = await page.EvaluateAsync<string[]>("__depgraphDebug.cy.nodes('.manual-search-match').map(node => node.id())");
        var expected = await page.EvaluateAsync<string[]>("([query]) => __depgraphDebug.cy.nodes().filter(node => node.data('label').toLowerCase().includes(query)).map(node => node.id())", new object[] { query });
        Assert.NotEmpty(matches);
        Assert.Equal(expected.Order(), matches.Order());

        var selected = await page.EvaluateAsync<string>("__depgraphDebug.cy.nodes().filter(node => !node.hasClass('manual-search-match'))[0]?.id() || __depgraphDebug.cy.nodes()[0].id()");
        await SelectIdsAsync(page, selected);
        await page.EvaluateAsync("__depgraphDebug.manualView.paint()");
        Assert.Equal(matches.Order(), (await page.EvaluateAsync<string[]>("__depgraphDebug.cy.nodes('.manual-search-match').map(node => node.id())")).Order());

        await search.FillAsync("");
        Assert.Equal(0, await page.EvaluateAsync<int>("__depgraphDebug.cy.nodes('.manual-search-match').length"));
    }

    [Fact]
    public async Task NewGroupAssignmentAndPortableSaveLoadRoundTrip()
    {
        await using var session = await BrowserSession.CreateAsync();
        var page = session.Page;
        await EnterManualAsync(page, startUnassigned: true);
        var first = await NodePointAsync(page, 0); var second = await NodePointAsync(page, 1);
        await page.Mouse.ClickAsync(first.X, first.Y);
        await page.Keyboard.DownAsync("Control"); await page.Mouse.ClickAsync(second.X, second.Y); await page.Keyboard.UpAsync("Control");
        Assert.Equal(2, await page.EvaluateAsync<int>("__depgraphDebug.cy.nodes(':selected').length"));

        await page.Locator("#manual-new-group").ClickAsync();
        await page.Locator("#manual-group-name").FillAsync("Feature A");
        await page.Locator("#manual-group-shape").SelectOptionAsync("circle");
        await page.Locator("#manual-group-create").ClickAsync();
        Assert.Equal(2, await page.EvaluateAsync<int>("Object.keys(__depgraphDebug.manualView.model.board.groups).length"));
        Assert.Empty(await BoardErrorsAsync(page));

        var download = await page.RunAndWaitForDownloadAsync(() => page.Locator("#manual-save").ClickAsync());
        var path = await download.PathAsync();
        await page.Locator("#manual-mute").ClickAsync();
        Assert.NotEmpty(await page.EvaluateAsync<string[]>("__depgraphDebug.manualView.model.board.mutedEntityIds"));
        await page.Locator("#manual-load-file").SetInputFilesAsync(path);
        await page.WaitForFunctionAsync("__depgraphDebug.manualView.model.board.mutedEntityIds.length === 0");
        Assert.Empty(await BoardErrorsAsync(page));
    }

    [Fact]
    public async Task NodeContextMenuRemovesAndMovesNodeBetweenGroups()
    {
        await using var session = await BrowserSession.CreateAsync();
        var page = session.Page;
        await EnterManualAsync(page, startUnassigned: true);
        var ids = await page.EvaluateAsync<string[]>("__depgraphDebug.cy.nodes().slice(0, 2).map(node => node.id())");
        await CreateGroupAsync(page, "Feature One", ids[0]);
        var firstGroup = await page.EvaluateAsync<string>("Object.keys(__depgraphDebug.manualView.model.board.groups).find(id => id !== 'group:unassigned')");
        await CreateGroupAsync(page, "Feature Two", ids[1]);
        var secondGroup = await page.EvaluateAsync<string>("([first]) => Object.keys(__depgraphDebug.manualView.model.board.groups).find(id => id !== 'group:unassigned' && id !== first)", new object[] { firstGroup });
        var node = await NodePointAsync(page, ids[0]);

        await page.Mouse.ClickAsync(node.X, node.Y, new() { Button = MouseButton.Right });

        Assert.True(await page.Locator("#manual-node-menu").IsVisibleAsync());
        Assert.Equal(ids[0], await page.EvaluateAsync<string>("__depgraphDebug.cy.nodes(':selected')[0].id()"));
        await page.Locator("#manual-node-remove-group").ClickAsync();
        Assert.Equal("group:unassigned", await page.EvaluateAsync<string>("([id]) => __depgraphDebug.manualView.model.board.placements[id].groupId", new object[] { ids[0] }));
        Assert.False(await page.Locator("#manual-node-menu").IsVisibleAsync());

        node = await NodePointAsync(page, ids[0]);
        await page.Mouse.ClickAsync(node.X, node.Y, new() { Button = MouseButton.Right });
        Assert.True(await page.Locator("#manual-node-remove-group").IsDisabledAsync());
        await page.Locator($"#manual-node-move-options button[data-group-id='{secondGroup}']").ClickAsync();

        Assert.Equal(secondGroup, await page.EvaluateAsync<string>("([id]) => __depgraphDebug.manualView.model.board.placements[id].groupId", new object[] { ids[0] }));
        Assert.Empty(await BoardErrorsAsync(page));
    }

    [Fact]
    public async Task NodeContextMenuCreatesAGroupFromTheCurrentSelection()
    {
        await using var session = await BrowserSession.CreateAsync();
        var page = session.Page;
        await EnterManualAsync(page, startUnassigned: true);
        var ids = await page.EvaluateAsync<string[]>("__depgraphDebug.cy.nodes().slice(0, 2).map(node => node.id())");
        await SelectIdsAsync(page, ids);
        var node = await NodePointAsync(page, ids[1]);

        await page.Mouse.ClickAsync(node.X, node.Y, new() { Button = MouseButton.Right });

        Assert.Equal(ids.Order(), (await SelectedIdsAsync(page)).Order());
        await page.Locator("#manual-node-create-group").ClickAsync();
        Assert.False(await page.Locator("#manual-node-menu").IsVisibleAsync());
        Assert.True(await page.Locator("#manual-context-group-dialog").IsVisibleAsync());
        Assert.False(await page.Locator("#manual-group-editor").IsVisibleAsync());
        Assert.Equal("2 selected nodes", await page.Locator("#manual-context-group-summary").TextContentAsync());
        await page.WaitForFunctionAsync("document.getElementById('manual-context-group-color').dataset.colorisReady === 'true'");
        Assert.Equal("text", await page.Locator("#manual-context-group-color").GetAttributeAsync("type"));
        await page.Locator("#manual-context-group-color").ClickAsync();
        Assert.True(await page.Locator(".clr-picker").IsVisibleAsync());
        Assert.True(await page.Locator(".clr-picker").EvaluateAsync<bool>("picker => getComputedStyle(picker).borderTopWidth === '1px' && getComputedStyle(picker).boxShadow !== 'none'"));
        Assert.True(await page.Locator("#clr-hue-slider").EvaluateAsync<bool>("slider => { const track=slider.parentElement.getBoundingClientRect(), control=slider.getBoundingClientRect(); return Math.abs(control.width - track.width - 32) < 0.5 && getComputedStyle(slider).marginTop === '0px'; }"));
        Assert.True(await page.Locator("#manual-context-group-dialog").EvaluateAsync<bool>("dialog => { dialog.scrollTop=100; return getComputedStyle(dialog).overflowY === 'visible' && dialog.scrollTop === 0; }"));
        Assert.Equal("OK", await page.Locator("#clr-close").TextContentAsync());
        Assert.True(await page.Locator("#clr-close").EvaluateAsync<bool>("element => element.closest('dialog')?.id === 'manual-context-group-dialog'"));
        await page.Locator("#clr-close").ClickAsync();
        Assert.False(await page.Locator(".clr-picker").IsVisibleAsync());
        await page.Locator("#manual-context-group-name").FillAsync("Context group");
        await page.Locator("#manual-context-group-color").FillAsync("#ff8800");
        await page.Locator("#manual-context-group-shape").SelectOptionAsync("circle");
        await page.Locator("#manual-context-group-create").ClickAsync();

        Assert.False(await page.Locator("#manual-context-group-dialog").IsVisibleAsync());
        Assert.True(await page.EvaluateAsync<bool>("([ids]) => { const board=__depgraphDebug.manualView.model.board, groupId=board.placements[ids[0]].groupId; return groupId !== 'group:unassigned' && board.placements[ids[1]].groupId === groupId && board.groups[groupId].name === 'Context group'; }", new object[] { ids }));
        Assert.Equal("circle", await page.EvaluateAsync<string>("([id]) => { const board=__depgraphDebug.manualView.model.board; return board.groups[board.placements[id].groupId].shape; }", new object[] { ids[0] }));
        Assert.Equal("#ff8800", await page.EvaluateAsync<string>("([id]) => { const board=__depgraphDebug.manualView.model.board; return board.groups[board.placements[id].groupId].color; }", new object[] { ids[0] }));
        Assert.Empty(await BoardErrorsAsync(page));
    }

    [Fact]
    public async Task GroupHeaderControlsCollapseAndControlOuterEdgesPersistently()
    {
        await using var session = await BrowserSession.CreateAsync();
        var page = session.Page;
        await EnterManualAsync(page, startUnassigned: true);
        var endpoints = await page.EvaluateAsync<string[]>("[__depgraphDebug.cy.edges()[0].source().id(), __depgraphDebug.cy.edges()[0].target().id()]");
        await CreateGroupAsync(page, "Source Feature", endpoints[0]);
        await CreateGroupAsync(page, "Target Feature", endpoints[1]);
        await page.EvaluateAsync("__depgraphDebug.manualView.fitBoard(__depgraphDebug.manualView.model.board)");
        var sourceGroup = await page.EvaluateAsync<string>("([id]) => __depgraphDebug.manualView.model.board.placements[id].groupId", new object[] { endpoints[0] });
        var outerEdge = $"([ids]) => __depgraphDebug.cy.$id(ids[0]).edgesWith(__depgraphDebug.cy.$id(ids[1]))";

        await page.Locator($".manual-region[data-group-id='{sourceGroup}'] .manual-region-action[data-action='highlight-outer']").ClickAsync();
        Assert.Equal("highlighted", await page.EvaluateAsync<string>("([id]) => __depgraphDebug.manualView.model.board.groups[id].outerEdgeMode", new object[] { sourceGroup }));
        Assert.True(await page.EvaluateAsync<bool>($"([ids]) => ({outerEdge})(ids).every(edge => edge.hasClass('manual-outer-highlight'))", new object[] { endpoints }));

        await page.Locator($".manual-region[data-group-id='{sourceGroup}'] .manual-region-action[data-action='collapse']").ClickAsync();
        Assert.True(await page.EvaluateAsync<bool>("([id]) => __depgraphDebug.manualView.model.board.groups[id].collapsed", new object[] { sourceGroup }));
        Assert.True(await page.EvaluateAsync<bool>("([id]) => __depgraphDebug.cy.$id(id).hasClass('manual-collapsed-member')", new object[] { endpoints[0] }));
        Assert.True(await page.EvaluateAsync<bool>($"([ids]) => ({outerEdge})(ids).every(edge => edge.style('display') === 'element')", new object[] { endpoints }));
        Assert.False(await page.Locator($".manual-region[data-group-id='{sourceGroup}'] .manual-region-body").IsVisibleAsync());
        Assert.False(await page.Locator($".manual-region[data-group-id='{sourceGroup}'] .manual-resize-handle").IsVisibleAsync());
        Assert.True(await page.Locator($".manual-region[data-group-id='{sourceGroup}'] .manual-region-header").IsVisibleAsync());
        Assert.True(await page.EvaluateAsync<bool>("([id]) => { const group=__depgraphDebug.manualView.model.board.groups[__depgraphDebug.manualView.model.board.placements[id].groupId], point=__depgraphDebug.cy.$id(id).position(); return Math.abs(point.x-(group.x+group.width/2)) < 0.001 && Math.abs(point.y-(group.y+group.header/2)) < 0.001; }", new object[] { endpoints[0] }));

        var download = await page.RunAndWaitForDownloadAsync(() => page.Locator("#manual-save").ClickAsync());
        var path = await download.PathAsync();
        await page.Locator($".manual-region[data-group-id='{sourceGroup}'] .manual-region-action[data-action='collapse']").ClickAsync();
        await page.Locator($".manual-region[data-group-id='{sourceGroup}'] .manual-region-action[data-action='highlight-outer']").ClickAsync();
        await page.Locator("#manual-load-file").SetInputFilesAsync(path);
        await page.WaitForFunctionAsync("([id]) => __depgraphDebug.manualView.model.board.groups[id]?.collapsed && __depgraphDebug.manualView.model.board.groups[id].outerEdgeMode === 'highlighted'", new object[] { sourceGroup });

        await page.Locator($".manual-region[data-group-id='{sourceGroup}'] .manual-region-action[data-action='hide-outer']").ClickAsync();
        Assert.Equal("hidden", await page.EvaluateAsync<string>("([id]) => __depgraphDebug.manualView.model.board.groups[id].outerEdgeMode", new object[] { sourceGroup }));
        Assert.True(await page.EvaluateAsync<bool>($"([ids]) => ({outerEdge})(ids).every(edge => edge.hasClass('manual-group-outer-hidden') && edge.style('display') === 'none')", new object[] { endpoints }));
        Assert.False(await page.EvaluateAsync<bool>($"([ids]) => ({outerEdge})(ids).some(edge => edge.hasClass('manual-outer-highlight'))", new object[] { endpoints }));

        await page.Locator($".manual-region[data-group-id='{sourceGroup}'] .manual-region-action[data-action='hide-outer']").ClickAsync();
        Assert.Equal("visible", await page.EvaluateAsync<string>("([id]) => __depgraphDebug.manualView.model.board.groups[id].outerEdgeMode", new object[] { sourceGroup }));
        Assert.True(await page.EvaluateAsync<bool>($"([ids]) => ({outerEdge})(ids).every(edge => edge.style('display') === 'element')", new object[] { endpoints }));
        Assert.Empty(await BoardErrorsAsync(page));
    }

    [Theory]
    [InlineData("rectangle")]
    [InlineData("circle")]
    public async Task NewGroupSizesToFitAllSelectedNodesAndExtractsThem(string shape)
    {
        await using var session = await BrowserSession.CreateAsync();
        var page = session.Page;
        await EnterManualAsync(page, startUnassigned: true);
        var ids = await page.EvaluateAsync<string[]>("__depgraphDebug.cy.nodes().map(node => node.id())");
        await CreateGroupAsync(page, "First source", ids[0], ids[1]);
        await CreateGroupAsync(page, "Second source", ids[2], ids[3]);
        var sourceRight = await page.EvaluateAsync<double>("Math.max(...Object.values(__depgraphDebug.manualView.model.board.groups).map(DepGraphManualGeometry.envelope).map(envelope => envelope.right))");
        var originalAssignments = await page.EvaluateAsync<string>("JSON.stringify(Object.fromEntries(Object.entries(__depgraphDebug.manualView.model.board.placements).map(([id, placement]) => [id, placement.groupId])))");
        await SelectIdsAsync(page, ids);

        await page.Locator("#manual-new-group").ClickAsync();
        await page.Locator("#manual-group-name").FillAsync($"Large {shape} group");
        await page.Locator("#manual-group-shape").SelectOptionAsync(shape);
        await page.Locator("#manual-group-create").ClickAsync();

        var groupId = await page.EvaluateAsync<string>("([id]) => __depgraphDebug.manualView.model.board.placements[id].groupId", new object[] { ids[0] });
        Assert.NotEqual("group:unassigned", groupId);
        Assert.Equal(ids.Length, await page.EvaluateAsync<int>("([groupId]) => Object.values(__depgraphDebug.manualView.model.board.placements).filter(placement => placement.groupId === groupId).length", new object[] { groupId }));
        Assert.True(await page.EvaluateAsync<bool>("([ids, groupId]) => ids.every(id => __depgraphDebug.manualView.model.board.placements[id].groupId === groupId)", new object[] { ids, groupId }));
        var created = await GroupGeometryAsync(page, groupId);
        Assert.True(created.Left >= sourceRight + 18);
        Assert.True(shape == "circle" ? created.Width > 220 : created.Width > 240 || created.Height > 220);
        Assert.False(await page.Locator("#warning").IsVisibleAsync());
        Assert.Empty(await BoardErrorsAsync(page));

        await page.Locator("#manual-undo").ClickAsync();
        Assert.Equal(originalAssignments, await page.EvaluateAsync<string>("JSON.stringify(Object.fromEntries(Object.entries(__depgraphDebug.manualView.model.board.placements).map(([id, placement]) => [id, placement.groupId])))"));
        Assert.False(await page.EvaluateAsync<bool>("([groupId]) => !!__depgraphDebug.manualView.model.board.groups[groupId]", new object[] { groupId }));
    }

    [Fact]
    public async Task LocalLayoutSettlesWithoutBreakingContainment()
    {
        await using var session = await BrowserSession.CreateAsync();
        var page = session.Page;
        await EnterManualAsync(page, startUnassigned: true);
        await page.Locator("#manual-run").ClickAsync();
        await page.WaitForFunctionAsync("document.getElementById('manual-run-status').textContent !== 'Layout running'", null, new() { Timeout = 15_000 });
        Assert.Empty(await BoardErrorsAsync(page));
        Assert.Equal(0, await page.EvaluateAsync<int>("__depgraphDebug.manualView.layout.running.size"));
    }

    [Fact]
    public async Task GroupCanBeDraggedAfterLayoutSettles()
    {
        await using var session = await BrowserSession.CreateAsync();
        var page = session.Page;
        await EnterManualAsync(page);
        await page.Locator("#manual-run").ClickAsync();
        await page.WaitForFunctionAsync("document.getElementById('manual-run-status').textContent !== 'Layout running'", null, new() { Timeout = 15_000 });
        Assert.Empty(await BoardErrorsAsync(page));
        var groupId = await page.EvaluateAsync<string>("Object.keys(__depgraphDebug.manualView.model.board.groups)[0]");
        var before = await GroupGeometryAsync(page, groupId);
        await DragAsync(page, page.Locator($".manual-region[data-group-id='{groupId}'] .manual-region-header"), 55, 35);
        var after = await GroupGeometryAsync(page, groupId);
        Assert.NotEqual(before.Left, after.Left); Assert.NotEqual(before.Top, after.Top);
        Assert.Empty(await BoardErrorsAsync(page));
    }

    [Fact]
    public async Task PauseButtonTogglesResumeAndRegionBodySelectsGroup()
    {
        await using var session = await BrowserSession.CreateAsync();
        var page = session.Page;
        await EnterManualAsync(page, startUnassigned: true);
        await page.Locator("#manual-run").ClickAsync();
        await page.Locator("#manual-pause").ClickAsync();
        Assert.Equal("Resume layout", await page.Locator("#manual-pause").TextContentAsync());
        Assert.Contains("paused", await page.Locator("#manual-run-status").TextContentAsync() ?? "", StringComparison.OrdinalIgnoreCase);
        await page.Locator("#manual-pause").ClickAsync();
        Assert.Equal("Pause layout", await page.Locator("#manual-pause").TextContentAsync());
        await page.Locator("#manual-pause").ClickAsync();

        var point = await page.EvaluateAsync<NodePoint>("() => { const g=__depgraphDebug.manualView.model.board.groups['group:unassigned'], p=g.shape==='circle'?{x:g.cx,y:g.cy}:{x:g.x+g.width-12,y:g.y+g.header+12}, r=document.getElementById('cy').getBoundingClientRect(), z=__depgraphDebug.cy.zoom(), pan=__depgraphDebug.cy.pan(); return {id:'group:unassigned',x:r.left+pan.x+p.x*z,y:r.top+pan.y+p.y*z}; }");
        await page.Mouse.ClickAsync(point.X, point.Y);
        Assert.Equal("group:unassigned", await page.EvaluateAsync<string>("__depgraphDebug.manualView.selectedGroupId"));
    }

    [Fact]
    public async Task WarningCanBeDismissedAndResizeRepacksForSmallerRegion()
    {
        await using var session = await BrowserSession.CreateAsync();
        var page = session.Page;
        await EnterManualAsync(page, startUnassigned: true);
        await page.Locator("#manual-new-group").ClickAsync();
        Assert.True(await page.Locator("#warning").IsVisibleAsync());
        await page.Locator("#warning-close").ClickAsync();
        Assert.False(await page.Locator("#warning").IsVisibleAsync());

        await page.Locator("#manual-run").ClickAsync();
        await page.WaitForFunctionAsync("document.getElementById('manual-run-status').textContent !== 'Layout running'", null, new() { Timeout = 15_000 });
        var before = await GroupGeometryAsync(page, "group:unassigned");
        var positions = await page.EvaluateAsync<string>("JSON.stringify(__depgraphDebug.manualView.model.board.placements)");
        await DragAsync(page, page.Locator(".manual-region[data-group-id='group:unassigned'] .manual-resize-handle"), -90, -75);
        var after = await GroupGeometryAsync(page, "group:unassigned");
        Assert.True(after.Width < before.Width || after.Height < before.Height);
        Assert.NotEqual(positions, await page.EvaluateAsync<string>("JSON.stringify(__depgraphDebug.manualView.model.board.placements)"));
        Assert.Empty(await BoardErrorsAsync(page));
    }

    [Fact]
    public async Task TabSwitchRestoresIndependentSelectionAndGeometry()
    {
        await using var session = await BrowserSession.CreateAsync();
        var page = session.Page;
        var exploreId = await page.EvaluateAsync<string>("__depgraphDebug.cy.nodes()[0].id()");
        await page.EvaluateAsync("([id]) => __depgraphDebug.cy.$id(id).select()", new object[] { exploreId });
        await EnterManualAsync(page, startUnassigned: true);
        var explorePosition = await page.EvaluateAsync<Position>("([id]) => __depgraphDebug.manualView.explore.nodes[id].position", new object[] { exploreId });
        var manual = await NodePointAsync(page, 1);
        await page.Mouse.ClickAsync(manual.X, manual.Y);
        var manualId = manual.Id;
        await page.Mouse.MoveAsync(manual.X, manual.Y); await page.Mouse.DownAsync(); await page.Mouse.MoveAsync(manual.X + 35, manual.Y + 20, new() { Steps = 5 }); await page.Mouse.UpAsync();
        var manualPosition = await page.EvaluateAsync<Position>("([id]) => __depgraphDebug.manualView.model.board.placements[id]", new object[] { manualId });

        await page.Locator("#tab-explore").ClickAsync();
        Assert.Equal(exploreId, await page.EvaluateAsync<string>("__depgraphDebug.cy.nodes(':selected')[0].id()"));
        var restoredExplorePosition = await page.EvaluateAsync<Position>("([id]) => __depgraphDebug.cy.$id(id).position()", new object[] { exploreId });
        Assert.Equal(explorePosition.X, restoredExplorePosition.X, 6); Assert.Equal(explorePosition.Y, restoredExplorePosition.Y, 6);
        await page.Locator("#tab-manual").ClickAsync();
        Assert.Equal(manualId, await page.EvaluateAsync<string>("__depgraphDebug.cy.nodes(':selected')[0].id()"));
        var restoredManualPosition = await page.EvaluateAsync<Position>("([id]) => __depgraphDebug.manualView.model.board.placements[id]", new object[] { manualId });
        Assert.Equal(manualPosition.X, restoredManualPosition.X, 6); Assert.Equal(manualPosition.Y, restoredManualPosition.Y, 6);
        Assert.True(await page.EvaluateAsync<bool>("__depgraphDebug.state.paused"));
    }

    [Fact]
    public async Task ManualRegionsAreHiddenInExploreAndRestoredInManualView()
    {
        await using var session = await BrowserSession.CreateAsync();
        var page = session.Page;
        await EnterManualAsync(page, startUnassigned: true);
        var regions = page.Locator("#manual-regions");

        Assert.True(await regions.Locator(".manual-region").CountAsync() > 0);
        Assert.True(await regions.IsVisibleAsync());

        await page.Locator("#tab-explore").ClickAsync();

        Assert.False(await regions.IsVisibleAsync());
        Assert.Equal("true", await regions.GetAttributeAsync("aria-hidden"));

        await page.Locator("#tab-manual").ClickAsync();

        Assert.True(await regions.IsVisibleAsync());
        Assert.Equal("false", await regions.GetAttributeAsync("aria-hidden"));
    }

    [Fact]
    public async Task ConnectionPolicyHidesEitherEndpointAndCanRevealTemporarily()
    {
        await using var session = await BrowserSession.CreateAsync();
        var page = session.Page;
        await EnterManualAsync(page, startUnassigned: true);
        var endpoints = await page.EvaluateAsync<string[]>("[__depgraphDebug.cy.edges()[0].source().id(), __depgraphDebug.cy.edges()[0].target().id()]");
        await SelectIdsAsync(page, endpoints[0]); await page.Locator("#manual-mute").ClickAsync();
        Assert.True(await page.EvaluateAsync<bool>("([ids]) => __depgraphDebug.cy.$id(ids[0]).connectedEdges().every(edge => edge.hasClass('manual-hidden'))", new object[] { endpoints }));
        await page.Locator("#manual-reveal").ClickAsync();
        Assert.True(await page.EvaluateAsync<bool>("__depgraphDebug.cy.edges('.manual-reveal').length > 0"));
        await SelectIdsAsync(page, endpoints); await page.Locator("#manual-mute").ClickAsync();
        await SelectIdsAsync(page, endpoints[0]); await page.Locator("#manual-unmute").ClickAsync();
        Assert.True(await page.EvaluateAsync<bool>("([ids]) => __depgraphDebug.cy.$id(ids[0]).edgesWith(__depgraphDebug.cy.$id(ids[1])).every(edge => edge.hasClass('manual-hidden'))", new object[] { endpoints }));
        await page.Locator("#manual-restore-all").ClickAsync();
        Assert.Equal(0, await page.EvaluateAsync<int>("__depgraphDebug.cy.edges('.manual-hidden').length"));
    }

    [Fact]
    public async Task GroupEditingAssignmentMergeFitArrangeAndDeleteAreUndoable()
    {
        await using var session = await BrowserSession.CreateAsync();
        var page = session.Page;
        await EnterManualAsync(page, startUnassigned: true);
        var ids = await page.EvaluateAsync<string[]>("__depgraphDebug.cy.nodes().slice(0, 4).map(node => node.id())");
        await CreateGroupAsync(page, "Feature One", ids[0]);
        var firstGroup = await page.EvaluateAsync<string>("Object.keys(__depgraphDebug.manualView.model.board.groups).find(id => id !== 'group:unassigned')");
        await CreateGroupAsync(page, "Feature Two", ids[1]);
        var secondGroup = await page.EvaluateAsync<string>("([first]) => Object.keys(__depgraphDebug.manualView.model.board.groups).find(id => id !== 'group:unassigned' && id !== first)", new object[] { firstGroup });
        await SelectIdsAsync(page, ids[2]); await page.Locator("#manual-assign-target").SelectOptionAsync(firstGroup); await page.Locator("#manual-assign").ClickAsync();
        Assert.Equal(firstGroup, await page.EvaluateAsync<string>("([id]) => __depgraphDebug.manualView.model.board.placements[id].groupId", new object[] { ids[2] }));

        await SelectIdsAsync(page, ids[0]);
        await page.Locator("#manual-edit-name").FillAsync("Renamed Feature"); await page.Locator("#manual-edit-color").FillAsync("#ff8800"); await page.Locator("#manual-update-group").ClickAsync();
        Assert.Equal("Renamed Feature", await page.EvaluateAsync<string>("([id]) => __depgraphDebug.manualView.model.board.groups[id].name", new object[] { firstGroup }));
        await page.Locator("#manual-fit-group").ClickAsync(); await page.Locator("#manual-arrange").ClickAsync(); Assert.Empty(await BoardErrorsAsync(page));

        await SelectIdsAsync(page, ids[0], ids[1]); await page.Locator("#manual-merge-groups").ClickAsync();
        Assert.Equal(2, await page.EvaluateAsync<int>("Object.keys(__depgraphDebug.manualView.model.board.groups).length"));
        Assert.Equal(await page.EvaluateAsync<string>("([id]) => __depgraphDebug.manualView.model.board.placements[id].groupId", new object[] { ids[0] }), await page.EvaluateAsync<string>("([id]) => __depgraphDebug.manualView.model.board.placements[id].groupId", new object[] { ids[1] }));
        page.Dialog += async (_, dialog) => await dialog.AcceptAsync();
        await page.Locator("#manual-delete-group").ClickAsync();
        Assert.Equal("group:unassigned", await page.EvaluateAsync<string>("([id]) => __depgraphDebug.manualView.model.board.placements[id].groupId", new object[] { ids[0] }));
        await page.Locator("#manual-undo").ClickAsync(); Assert.Equal(2, await page.EvaluateAsync<int>("Object.keys(__depgraphDebug.manualView.model.board.groups).length"));
        Assert.Empty(await BoardErrorsAsync(page));
    }

    [Fact]
    public async Task UndoDeletedGroupRestoresItsInternalAndExternalConnections()
    {
        await using var session = await BrowserSession.CreateAsync();
        var page = session.Page;
        await EnterManualAsync(page, startUnassigned: true);
        var ids = await page.EvaluateAsync<string[]>("() => { const source=__depgraphDebug.cy.nodes().filter(node => node.neighborhood('node').length >= 2)[0], neighbors=source.neighborhood('node'); return [source.id(), neighbors[0].id(), neighbors[1].id()]; }");
        await CreateGroupAsync(page, "Restored Feature", ids[0], ids[1]);
        var groupId = await page.EvaluateAsync<string>("([id]) => __depgraphDebug.manualView.model.board.placements[id].groupId", new object[] { ids[0] });
        page.Dialog += async (_, dialog) => await dialog.AcceptAsync();

        await page.Locator("#manual-delete-group").ClickAsync();
        await page.Locator("#manual-undo").ClickAsync();

        Assert.True(await page.EvaluateAsync<bool>("([id]) => !!__depgraphDebug.manualView.model.board.groups[id]", new object[] { groupId }));
        Assert.True(await page.EvaluateAsync<bool>("([ids]) => { const cy=__depgraphDebug.cy, internal=cy.$id(ids[0]).edgesWith(cy.$id(ids[1])), external=cy.$id(ids[0]).edgesWith(cy.$id(ids[2])); return internal.length > 0 && external.length > 0 && internal.union(external).every(edge => edge.style('display') === 'element' && Number(edge.style('opacity')) > 0); }", new object[] { ids }));
    }

    [Fact]
    public async Task DeletingGroupsExpandsAndRelocatesUnassignedWhenNeeded()
    {
        await using var session = await BrowserSession.CreateAsync();
        var page = session.Page;
        await EnterManualAsync(page);
        var initiallyUnassigned = await page.EvaluateAsync<string[]>("Object.entries(__depgraphDebug.manualView.model.board.placements).filter(([, placement]) => placement.groupId === 'group:unassigned').map(([id]) => id)");
        if (initiallyUnassigned.Length > 0)
            await CreateGroupAsync(page, "Formerly Unassigned", initiallyUnassigned);
        var groupIds = await page.EvaluateAsync<string[]>("Object.keys(__depgraphDebug.manualView.model.board.groups).filter(id => id !== 'group:unassigned')");
        await page.EvaluateAsync("() => __depgraphDebug.manualView.model.transact('Constrain Unassigned', board => { const group=board.groups['group:unassigned']; group.width=90; group.height=group.header+60; })");
        var before = await GroupGeometryAsync(page, "group:unassigned");
        page.Dialog += async (_, dialog) => await dialog.AcceptAsync();

        foreach (var groupId in groupIds)
        {
            await page.EvaluateAsync("([id]) => __depgraphDebug.manualView.selectGroup(id)", new object[] { groupId });
            await page.Locator("#manual-delete-group").ClickAsync();
        }

        var after = await GroupGeometryAsync(page, "group:unassigned");
        Assert.True(after.Width > before.Width || after.Height > before.Height);
        Assert.True(await page.EvaluateAsync<bool>("Object.values(__depgraphDebug.manualView.model.board.placements).every(placement => placement.groupId === 'group:unassigned')"));
        Assert.Empty(await BoardErrorsAsync(page));
    }

    [Fact]
    public async Task StartOverReplacesActiveBoardBeforeSubsequentEdits()
    {
        await using var session = await BrowserSession.CreateAsync();
        var page = session.Page;
        await EnterManualAsync(page, startUnassigned: true);
        var ids = await page.EvaluateAsync<string[]>("__depgraphDebug.cy.nodes().slice(0, 4).map(node => node.id())");
        await CreateGroupAsync(page, "Pre-reset group", ids[0], ids[1]);
        Assert.True(await page.EvaluateAsync<bool>("Object.values(__depgraphDebug.manualView.model.board.groups).some(group => group.name === 'Pre-reset group')"));
        page.Dialog += async (_, dialog) => await dialog.AcceptAsync();

        await page.Locator("#manual-start-over").ClickAsync();

        Assert.False(await page.EvaluateAsync<bool>("Object.values(__depgraphDebug.manualView.model.board.groups).some(group => group.name === 'Pre-reset group')"));
        Assert.True(await page.Locator("#manual-preview-actions").IsHiddenAsync());
        Assert.True(await page.EvaluateAsync<bool>("__depgraphDebug.manualView.preview === null"));
        await CreateGroupAsync(page, "Post-reset group", ids[2], ids[3]);

        Assert.False(await page.EvaluateAsync<bool>("Object.values(__depgraphDebug.manualView.model.board.groups).some(group => group.name === 'Pre-reset group')"));
        Assert.True(await page.EvaluateAsync<bool>("Object.values(__depgraphDebug.manualView.model.board.groups).some(group => group.name === 'Post-reset group')"));
        Assert.Empty(await BoardErrorsAsync(page));
    }

    [Fact]
    public async Task ResetToUnassignedReplacesTheBoardAndClearsManualState()
    {
        await using var session = await BrowserSession.CreateAsync();
        var page = session.Page;
        await EnterManualAsync(page, startUnassigned: true);
        var ids = await page.EvaluateAsync<string[]>("__depgraphDebug.cy.nodes().slice(0, 2).map(node => node.id())");
        await CreateGroupAsync(page, "Feature One", ids[0]);
        await CreateGroupAsync(page, "Feature Two", ids[1]);
        await page.EvaluateAsync("() => { const view=__depgraphDebug.manualView, groupIds=Object.keys(view.model.board.groups).filter(id => id !== 'group:unassigned'); view.model.addSupergroup('Product Area', '#6f42c1', groupIds); }");
        await SelectIdsAsync(page, ids[0]);
        await page.Locator("#manual-mute").ClickAsync();
        var previousLayoutId = await page.EvaluateAsync<string>("__depgraphDebug.manualView.model.board.layoutId");
        var resetButtonLayout = await page.EvaluateAsync<double[]>("() => { const first=document.getElementById('manual-start-over').getBoundingClientRect(), second=document.getElementById('manual-reset-unassigned').getBoundingClientRect(); return [first.width, second.width, first.height, second.height, second.top-first.bottom]; }");
        Assert.InRange(Math.Abs(resetButtonLayout[0] - resetButtonLayout[1]), 0, 0.5);
        Assert.InRange(Math.Abs(resetButtonLayout[2] - resetButtonLayout[3]), 0, 0.5);
        Assert.True(resetButtonLayout[4] >= 8);
        page.Dialog += async (_, dialog) => await dialog.AcceptAsync();

        await page.Locator("#manual-reset-unassigned").ClickAsync();

        Assert.NotEqual(previousLayoutId, await page.EvaluateAsync<string>("__depgraphDebug.manualView.model.board.layoutId"));
        Assert.Equal(new[] { "group:unassigned" }, await page.EvaluateAsync<string[]>("Object.keys(__depgraphDebug.manualView.model.board.groups)"));
        Assert.True(await page.EvaluateAsync<bool>("Object.values(__depgraphDebug.manualView.model.board.placements).every(placement => placement.groupId === 'group:unassigned')"));
        Assert.Equal(0, await page.EvaluateAsync<int>("Object.keys(__depgraphDebug.manualView.model.board.supergroups).length"));
        Assert.Equal(0, await page.EvaluateAsync<int>("__depgraphDebug.manualView.model.board.mutedEntityIds.length"));
        Assert.Equal(0, await page.EvaluateAsync<int>("__depgraphDebug.cy.nodes(':selected').length"));
        Assert.Empty(await BoardErrorsAsync(page));
    }

    [Fact]
    public async Task SupergroupsMovePersistAndDissolveWithoutMergingChildGroups()
    {
        await using var session = await BrowserSession.CreateAsync();
        var page = session.Page;
        await EnterManualAsync(page, startUnassigned: true);
        var ids = await page.EvaluateAsync<string[]>("__depgraphDebug.cy.nodes().slice(0, 2).map(node => node.id())");
        await CreateGroupAsync(page, "Feature One", ids[0]);
        await CreateGroupAsync(page, "Feature Two", ids[1]);
        var groupIds = await page.EvaluateAsync<string[]>("Object.keys(__depgraphDebug.manualView.model.board.groups).filter(id => id !== 'group:unassigned')");

        await page.Locator($"#manual-group-list button[data-group-id='{groupIds[0]}']").ClickAsync();
        await page.Keyboard.DownAsync("Control");
        await page.Locator($"#manual-group-list button[data-group-id='{groupIds[1]}']").ClickAsync();
        await page.Keyboard.UpAsync("Control");
        Assert.False(await page.Locator("#manual-supergroup-create").IsDisabledAsync());
        await page.Locator("#manual-supergroup-name").FillAsync("Product Area");
        await page.Locator("#manual-supergroup-color").FillAsync("#6f42c1");
        await page.Locator("#manual-supergroup-create").ClickAsync();

        var supergroupId = await page.EvaluateAsync<string>("Object.keys(__depgraphDebug.manualView.model.board.supergroups)[0]");
        Assert.Equal(groupIds.Order(), (await page.EvaluateAsync<string[]>("([id]) => __depgraphDebug.manualView.model.board.supergroups[id].groupIds", new object[] { supergroupId })).Order());
        Assert.Equal(1, await page.Locator($".manual-supergroup[data-supergroup-id='{supergroupId}']").CountAsync());
        Assert.Equal(3, await page.EvaluateAsync<int>("Object.keys(__depgraphDebug.manualView.model.board.groups).length"));
        await page.EvaluateAsync("__depgraphDebug.manualView.fitBoard(__depgraphDebug.manualView.model.board)");
        await page.WaitForTimeoutAsync(50);
        var before = await page.EvaluateAsync<Position[]>("([ids]) => ids.map(id => { const e=DepGraphManualGeometry.envelope(__depgraphDebug.manualView.model.board.groups[id]); return {x:e.left,y:e.top}; })", new object[] { groupIds });
        var zoom = await page.EvaluateAsync<double>("__depgraphDebug.cy.zoom()");

        var supergroupHeader = page.Locator($".manual-supergroup[data-supergroup-id='{supergroupId}'] .manual-supergroup-header");
        await DragAsync(page, supergroupHeader, 70, 45);

        var moved = await page.EvaluateAsync<Position[]>("([ids]) => ids.map(id => { const e=DepGraphManualGeometry.envelope(__depgraphDebug.manualView.model.board.groups[id]); return {x:e.left,y:e.top}; })", new object[] { groupIds });
        for (var index = 0; index < groupIds.Length; index++)
        {
            Assert.InRange(moved[index].X - before[index].X, 65 / zoom, 75 / zoom);
            Assert.InRange(moved[index].Y - before[index].Y, 40 / zoom, 50 / zoom);
        }
        await page.Locator("#manual-undo").ClickAsync();
        var undone = await page.EvaluateAsync<Position[]>("([ids]) => ids.map(id => { const e=DepGraphManualGeometry.envelope(__depgraphDebug.manualView.model.board.groups[id]); return {x:e.left,y:e.top}; })", new object[] { groupIds });
        Assert.Equal(before[0].X, undone[0].X, 6);
        Assert.Equal(before[1].Y, undone[1].Y, 6);
        await page.Locator("#manual-redo").ClickAsync();

        var download = await page.RunAndWaitForDownloadAsync(() => page.Locator("#manual-save").ClickAsync());
        var path = await download.PathAsync();
        await page.Locator($"#manual-supergroup-list button[title='Dissolve Product Area']").ClickAsync();
        Assert.Equal(0, await page.EvaluateAsync<int>("Object.keys(__depgraphDebug.manualView.model.board.supergroups).length"));
        Assert.Equal(3, await page.EvaluateAsync<int>("Object.keys(__depgraphDebug.manualView.model.board.groups).length"));
        await page.Locator("#manual-load-file").SetInputFilesAsync(path);
        await page.WaitForFunctionAsync("([id]) => !!__depgraphDebug.manualView.model.board.supergroups[id]", new object[] { supergroupId });
        Assert.Equal("Product Area", await page.EvaluateAsync<string>("([id]) => __depgraphDebug.manualView.model.board.supergroups[id].name", new object[] { supergroupId }));
        Assert.Empty(await BoardErrorsAsync(page));
    }

    [Fact]
    public async Task LayoutsWithoutSupergroupsRemainLoadable()
    {
        await using var session = await BrowserSession.CreateAsync();
        var page = session.Page;
        await EnterManualAsync(page, startUnassigned: true);

        Assert.True(await page.EvaluateAsync<bool>("() => { const view=__depgraphDebug.manualView, document=DepGraphManualStorage.documentFromBoard(view.model.board); delete document.supergroups; const loaded=view.storage.importText(JSON.stringify(document)); return loaded.supergroups && Object.keys(loaded.supergroups).length === 0; }"));
    }

    private static async Task EnterManualAsync(IPage page, bool startUnassigned = false)
    {
        await page.Locator("#tab-manual").ClickAsync();
        await page.Locator(startUnassigned ? "#manual-create-unassigned" : "#manual-create-current").ClickAsync();
        await page.Locator("#manual-preview-accept").ClickAsync();
        await page.WaitForFunctionAsync("!!__depgraphDebug.manualView.model");
    }

    private static async Task SelectIdsAsync(IPage page, params string[] ids) => await page.EvaluateAsync("([ids]) => { __depgraphDebug.cy.nodes().unselect(); ids.forEach(id => __depgraphDebug.cy.$id(id).select()); }", new object[] { ids });
    private static async Task<string[]> SelectedIdsAsync(IPage page) => await page.EvaluateAsync<string[]>("__depgraphDebug.cy.nodes(':selected').map(node => node.id())");
    private static async Task CreateGroupAsync(IPage page, string name, params string[] ids)
    {
        await SelectIdsAsync(page, ids); await page.Locator("#manual-new-group").ClickAsync(); await page.Locator("#manual-group-name").FillAsync(name); await page.Locator("#manual-group-shape").SelectOptionAsync("rectangle"); await page.Locator("#manual-group-create").ClickAsync();
    }

    private static async Task<NodePoint> NodePointAsync(IPage page, int index) => await page.EvaluateAsync<NodePoint>("([index]) => { const n=__depgraphDebug.cy.nodes()[index], p=n.renderedPosition(), r=document.getElementById('cy').getBoundingClientRect(); return {id:n.id(),x:r.left+p.x,y:r.top+p.y}; }", new object[] { index });
    private static async Task<NodePoint> NodePointAsync(IPage page, string id) => await page.EvaluateAsync<NodePoint>("([id]) => { const n=__depgraphDebug.cy.$id(id), p=n.renderedPosition(), r=document.getElementById('cy').getBoundingClientRect(); return {id:n.id(),x:r.left+p.x,y:r.top+p.y}; }", new object[] { id });
    private static async Task<string[]> BoardErrorsAsync(IPage page) => await page.EvaluateAsync<string[]>("DepGraphManualGeometry.validate(__depgraphDebug.manualView.model.board)");
    private static async Task<GroupGeometry> GroupGeometryAsync(IPage page, string groupId) => await page.EvaluateAsync<GroupGeometry>("([id]) => { const e=DepGraphManualGeometry.envelope(__depgraphDebug.manualView.model.board.groups[id]); return {left:e.left,top:e.top,width:e.right-e.left,height:e.bottom-e.top}; }", new object[] { groupId });
    private static async Task<NodePoint> GroupBodyPointAsync(IPage page, string groupId) => await page.EvaluateAsync<NodePoint>("([id]) => { const group=__depgraphDebug.manualView.model.board.groups[id], area=DepGraphManualGeometry.usable(group), nodes=Object.entries(__depgraphDebug.manualView.model.board.placements).filter(([,placement]) => placement.groupId===id).map(([nodeId,placement]) => ({...placement,radius:__depgraphDebug.manualView.model.board.entities[nodeId].radius})), bounds=area.shape==='circle'?{left:area.cx-area.radius,right:area.cx+area.radius,top:area.cy-area.radius,bottom:area.cy+area.radius}:area; for(let y=bounds.top+24;y<=bounds.bottom-24;y+=12) for(let x=bounds.left+24;x<=bounds.right-24;x+=12) if(DepGraphManualGeometry.contains(group,{x,y},12)&&nodes.every(node=>Math.hypot(node.x-x,node.y-y)>node.radius+14)){ const rect=document.getElementById('cy').getBoundingClientRect(), zoom=__depgraphDebug.cy.zoom(), pan=__depgraphDebug.cy.pan(); return {id,x:rect.left+pan.x+x*zoom,y:rect.top+pan.y+y*zoom}; } throw new Error('No empty group-body point found.'); }", new object[] { groupId });
    private static async Task DragAsync(IPage page, ILocator locator, float dx, float dy)
    {
        var box = await locator.BoundingBoxAsync() ?? throw new InvalidOperationException("Drag target has no bounding box.");
        var x = box.X + box.Width / 2; var y = box.Y + box.Height / 2;
        await page.Mouse.MoveAsync(x, y); await page.Mouse.DownAsync(); await page.Mouse.MoveAsync(x + dx, y + dy, new() { Steps = 8 }); await page.Mouse.UpAsync();
    }

    private sealed class NodePoint { public string Id { get; set; } = ""; public float X { get; set; } public float Y { get; set; } }
    private sealed class Position { public double X { get; set; } public double Y { get; set; } }
    private sealed class GroupGeometry { public double Left { get; set; } public double Top { get; set; } public double Width { get; set; } public double Height { get; set; } }

    private sealed class BrowserSession : IAsyncDisposable
    {
        private readonly IPlaywright playwright;
        private readonly IBrowser browser;
        private readonly string reportDirectory;
        public IPage Page { get; }

        private BrowserSession(IPlaywright playwright, IBrowser browser, IPage page, string reportDirectory) => (this.playwright, this.browser, Page, this.reportDirectory) = (playwright, browser, page, reportDirectory);

        public static async Task<BrowserSession> CreateAsync()
        {
            var root = RepositoryRoot(); var report = Path.Combine(Path.GetTempPath(), "depgraph-browser-" + Guid.NewGuid().ToString("N"));
            var graph = JsonSerializer.Deserialize<DependencyGraph>(await File.ReadAllTextAsync(Path.Combine(root, "sample-output", "graph.json")), OutputWriter.JsonOptions) ?? throw new InvalidDataException("Sample graph is invalid.");
            OutputWriter.Write(report, graph, new([], [], [], [], FilterMode.Contract, Force: true), Path.Combine(root, "src", "DotNetDependencyGraph.Cli", "viewer"));
            var playwright = await Playwright.CreateAsync();
            var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
            var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 1440, Height = 900 }, AcceptDownloads = true });
            await page.GotoAsync(new Uri(Path.Combine(report, "index.html")).AbsoluteUri);
            await page.WaitForFunctionAsync("!!window.__depgraphDebug && __depgraphDebug.cy.nodes().length > 2");
            await page.EvaluateAsync("for(const key of Object.keys(localStorage)) if(key.startsWith('dotnet-depgraph.manual-layout.v1:')) localStorage.removeItem(key)");
            return new(playwright, browser, page, report);
        }

        public async ValueTask DisposeAsync()
        {
            await browser.DisposeAsync(); playwright.Dispose();
            if (Directory.Exists(reportDirectory)) Directory.Delete(reportDirectory, true);
        }

        private static string RepositoryRoot() { var directory = new DirectoryInfo(AppContext.BaseDirectory); while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DotNetDependencyGraph.slnx"))) directory = directory.Parent; return directory?.FullName ?? throw new InvalidOperationException("Repository root not found."); }
    }
}
