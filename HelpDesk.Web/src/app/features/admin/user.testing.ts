// Public DTO fixtures for simulated HTTP only.
import { PublicUser } from '../../core/auth/auth.models';
export function userFixture(overrides: Partial<PublicUser> = {}): PublicUser {
  return {
    id: 7,
    name: 'Fixture technician',
    email: 'technician@example.invalid',
    role: 'Technician',
    createdAt: '2026-11-04T08:00:00Z',
    ...overrides,
  };
}
