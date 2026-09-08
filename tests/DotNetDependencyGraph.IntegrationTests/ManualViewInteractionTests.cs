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
        await page.Mouse.ClickAsync(700, 820);
        Assert.Equal(0, await page.EvaluateAsync<int>("__depgraphDebug.cy.nodes(':selected').length"));
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
        Assert.Empty(await BoardErrorsAsync(page));
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

    private static async Task EnterManualAsync(IPage page, bool startUnassigned = false)
    {
        await page.Locator("#tab-manual").ClickAsync();
        await page.Locator(startUnassigned ? "#manual-create-unassigned" : "#manual-create-current").ClickAsync();
        await page.Locator("#manual-preview-accept").ClickAsync();
        await page.WaitForFunctionAsync("!!__depgraphDebug.manualView.model");
    }

    private static async Task SelectIdsAsync(IPage page, params string[] ids) => await page.EvaluateAsync("([ids]) => { __depgraphDebug.cy.nodes().unselect(); ids.forEach(id => __depgraphDebug.cy.$id(id).select()); }", new object[] { ids });
    private static async Task CreateGroupAsync(IPage page, string name, params string[] ids)
    {
        await SelectIdsAsync(page, ids); await page.Locator("#manual-new-group").ClickAsync(); await page.Locator("#manual-group-name").FillAsync(name); await page.Locator("#manual-group-shape").SelectOptionAsync("rectangle"); await page.Locator("#manual-group-create").ClickAsync();
    }

    private static async Task<NodePoint> NodePointAsync(IPage page, int index) => await page.EvaluateAsync<NodePoint>("([index]) => { const n=__depgraphDebug.cy.nodes()[index], p=n.renderedPosition(), r=document.getElementById('cy').getBoundingClientRect(); return {id:n.id(),x:r.left+p.x,y:r.top+p.y}; }", new object[] { index });
    private static async Task<string[]> BoardErrorsAsync(IPage page) => await page.EvaluateAsync<string[]>("DepGraphManualGeometry.validate(__depgraphDebug.manualView.model.board)");
    private static async Task<GroupGeometry> GroupGeometryAsync(IPage page, string groupId) => await page.EvaluateAsync<GroupGeometry>("([id]) => { const e=DepGraphManualGeometry.envelope(__depgraphDebug.manualView.model.board.groups[id]); return {left:e.left,top:e.top,width:e.right-e.left,height:e.bottom-e.top}; }", new object[] { groupId });
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
