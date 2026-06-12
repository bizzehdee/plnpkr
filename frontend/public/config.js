// Runtime configuration for the SPA — editable WITHOUT rebuilding the Angular bundle.
//
// For a SPLIT deployment (this SPA hosted on blob storage / S3 / a CDN, and the .NET API hosted
// separately on a server), set `apiBase` to the API's absolute origin, e.g.
//   window.__PP_CONFIG__ = { apiBase: "https://planning-poker-api.example.com" };
// The REST calls and the SignalR hub both derive their URL from this value.
//
// Leave it empty ("") when the SPA is served from the SAME origin as the API (the bundled
// single-artifact deploy) or during local `ng serve` — the app then infers the API origin.
window.__PP_CONFIG__ = { apiBase: "" };
