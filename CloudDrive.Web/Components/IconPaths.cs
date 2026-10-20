namespace CloudDrive.Web.Components;

public static class IconPaths
{
    private static readonly Dictionary<string, string> Paths = new()
    {
        ["upload"] = "M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4M17 8l-5-5-5 5M12 3v12",
        ["download"] = "M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4M7 10l5 5 5-5M12 15V3",
        ["trash"] = "M3 6h18M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2M10 11v6M14 11v6",
        ["refresh"] = "M23 4v6h-6M1 20v-6h6M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15",
        ["folder"] = "M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z",
        ["folder-plus"] = "M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2zM12 11v6M9 14h6",
        ["search"] = "M11 3a8 8 0 1 0 0 16 8 8 0 0 0 0-16zM21 21l-4.35-4.35",
        ["file"] = "M13 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V9zM13 2v7h7",
        ["file-text"] = "M13 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V9zM13 2v7h7M16 13H8M16 17H8M10 9H8",
        ["image"] = "M5 3h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2zM8.5 8.5h.01M21 15l-5-5L5 21",
        ["code"] = "M16 18l6-6-6-6M8 6l-6 6 6 6",
        ["cloud"] = "M18 10h-1.26A8 8 0 1 0 9 20h9a5 5 0 0 0 0-10z",
        ["x"] = "M18 6L6 18M6 6l12 12",
        ["check-circle"] = "M22 11.08V12a10 10 0 1 1-5.93-9.14M22 4L12 14.01l-3-3",
        ["alert"] = "M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0zM12 9v4M12 17h.01",
        ["columns"] = "M12 3h7a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2h-7m0-18H5a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h7m0-18v18",
        ["filter"] = "M22 3H2l8 9.46V19l4 2v-8.54L22 3z",
        ["sort"] = "M7 15l5 5 5-5M7 9l5-5 5 5",
        ["home"] = "M3 9l9-7 9 7v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2zM9 22V12h6v10",
        ["chevron-right"] = "M9 18l6-6-6-6",
        ["chevron-down"] = "M6 9l6 6 6-6",
        ["chevron-up"] = "M18 15l-6-6-6 6",
        ["sync"] = "M17 1l4 4-4 4M3 11V9a4 4 0 0 1 4-4h14M7 23l-4-4 4-4M21 13v2a4 4 0 0 1-4 4H3",
        ["log-out"] = "M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4M16 17l5-5-5-5M21 12H9",
        ["menu"] = "M3 12h18M3 6h18M3 18h18",
        ["info"] = "M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20zM12 16v-4M12 8h.01",
    };

    public static string Get(string name) => Paths.TryGetValue(name, out var path) ? path : "";
}
