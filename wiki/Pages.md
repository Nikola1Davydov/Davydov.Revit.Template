# Pages

The UI of the add-in is one React app in `source/Acme.Revit.Tools/ClientApp` (Vite, React, TanStack Query), shown in
WebView2. A page is a hash route of this app; one window class shows every page.

## Add a page

`ClientApp/src/App.tsx` picks the component by route (`useRoute` reads `location.hash`):

```tsx
if (route === "/families") return <FamiliesPage />;
```

Take a route library when the pages grow; keep hash routes — the add-in opens pages as `#/route`.

## Call commands from a page

Use the hooks of `ClientApp/src/revit/hooks.ts`:

```tsx
const document = useActiveDocument();                        // { key, title } or null
const families = useCommand<FamilyItem[]>("GetFamilies");    // reads; cached per document
const rename = useCommandMutation<void, RenameFamiliesRequest>("RenameFamilies");

rename.mutate({ ids, prefix: "FAM_" });
rename.progress;   // { fraction, message } while it runs
rename.cancel();
```

The hooks add the `Document` header, so a call made with data of one document fails with `document_changed` after the
user switches documents, and the page reloads its data. Do not call `bridge.invoke` directly — you would lose that.

## Show a page

In a window, from a ribbon button:

```csharp
Host.GetService<IWindows>().Open("/families", options =>
{
    options.Title = "Families";
    options.Width = 900;
    options.Height = 640;
});
```

* One window per route: opening the same route again brings its window to the front.
* Windows are modeless and owned by the Revit window.

In the dock pane: `Host.GetService<IWindows>().ShowDockPane("/families")`. The add-in has one dock pane, registered in
`Host.cs` with `AddWebView(application, w => w.DockPane(id, title, route))`. Keep its id: Revit remembers where the user
docked it.

## Develop without Revit

```
cd source/Acme.Revit.Tools/ClientApp
npm install
npm run dev
```

* **In a normal browser** (`http://localhost:5173`) the page talks to the mocks of `ClientApp/src/mocks.ts`. Add an
  answer there for every command a page calls.
* **In Revit, Debug build:** the windows load the same dev server (`ClientApp:DevServerUrl`, `http://localhost:5173` by
  default) with hot reload — change a component, see it in Revit at once. F12 opens DevTools.
* **Release build:** the pipeline runs `npm run build`, and the add-in ships `ClientApp/dist` and loads it from
  `https://app.acme-revit-tools.local`, a virtual host — no server, no network.

## Links and security

Any page loaded in the WebView can call your commands. With the navigation protection on, only the add-in's own pages
load: a link to another address — `target="_blank"` too — opens in the system browser, and messages from another origin
are dropped.

`AllowAnyNavigation()` in `AddWebView` turns the protection off. The template's `Host.cs` calls it; remove the call
unless a page really needs to load foreign content.

## Check

`npm run build` in `ClientApp` type-checks and builds. The bridge itself is a small package,
`source/Acme.Revit.Tools.Hosting.WebView/revit-bridge`, with its own Vitest tests (`npm test` there). In Revit: the button
opens the window, the page shows the active document's data, switching documents refreshes it.
