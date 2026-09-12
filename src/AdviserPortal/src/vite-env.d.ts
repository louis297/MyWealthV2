/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_IDENTITY_AUTHORITY: string;
  readonly VITE_WEBAPI_BASE_URL: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
