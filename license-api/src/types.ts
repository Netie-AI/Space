export interface Env {
  DB: D1Database;
  INSTALLERS?: R2Bucket;
  APP_NAME: string;
  CORS_ORIGINS: string;
  PUBLIC_BASE_URL: string;
  RESEND_API_KEY: string;
  OWNER_NOTIFY_EMAIL: string;
  RESEND_FROM_EMAIL: string;
  JWT_SECRET: string;
  DEVICE_PEPPER: string;
  STRIPE_SECRET_KEY?: string;
  STRIPE_PRICE_ID?: string;
  STRIPE_WEBHOOK_SECRET?: string;
}

export type Json = Record<string, unknown>;
