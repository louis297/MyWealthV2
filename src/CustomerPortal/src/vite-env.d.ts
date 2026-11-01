/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly BASE_URL: string;
  readonly VITE_BFF_HTTP?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
