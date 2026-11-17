export const AUTH_ROLES = {
  administrator: 'Administrador',
  user: 'Usuario'
} as const;

export interface DevHubPartner {
  partnerId: number;
  partnerName: string;
}

export interface SapDatabase {
  id: number;
  nomeBase: string;
  partnerId: number;
}

export interface DevHubSelection {
  partnerId: number;
  databases: SapDatabase[];
  serviceLayer: {
    url: string;
    userName: string;
    partnerId: number;
    partnerName: string;
  };
}

export interface DevHubLoginResponse {
  sessionKey: string;
  partners: DevHubPartner[];
}

export interface LoginRequest {
  partnerId: number;
  databaseId: number;
  password: string;
}

export interface LoginResponse {
  token: string;
  expiresAtUtc: string;
  nomeUsuario: string;
  tipoUsuario: string;
  database: string;
  idioma?: string | null;
}
