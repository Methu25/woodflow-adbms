export type Role = 'OWNER' | 'ASSISTANT';

export interface Account {
  userId: number;
  username: string;
  fullName: string;
  role: Role;
}

export interface LoginRequest {
  username: string;
  password: string;
}

export interface OwnerSetupRequest extends LoginRequest {
  fullName: string;
  confirmPassword: string;
  setupCode: string;
}
