export type UserRole = 'User' | 'Technician' | 'Admin';
export interface LoginRequest {
  email: string;
  password: string;
}
export interface RegisterRequest extends LoginRequest {
  name: string;
}
export interface PublicUser {
  id: number;
  name: string;
  email: string;
  role: UserRole;
  createdAt: string;
}
export interface SessionUser {
  id: number;
  email: string;
  role: UserRole;
}
export const roleLabels: Record<UserRole, string> = {
  User: 'Usuario',
  Technician: 'Técnico',
  Admin: 'Administrador',
};
export const roleNavigation: Record<UserRole, string> = {
  User: 'Mi espacio',
  Technician: 'Espacio técnico',
  Admin: 'Espacio administrador',
};
