import { FormControl } from '@angular/forms';
import { emailAddress, nonBlank, registrationPassword } from './auth.validation';

describe('Authentication form validation', () => {
  it.each(['', '   ', '\n'])('rejects blank required inputs', (value) =>
    expect(nonBlank(new FormControl(value))).not.toBeNull(),
  );
  it.each(['a'.repeat(15), 'a'.repeat(128), '😀'.repeat(8), ' leading and trailing '])(
    'accepts valid registration passwords without transformation',
    (value) => {
      const control = new FormControl(value);
      expect(registrationPassword(control)).toBeNull();
      expect(control.value).toBe(value);
    },
  );
  it.each([
    'a'.repeat(14),
    'a'.repeat(129),
    '😀'.repeat(7),
    'a'.repeat(15) + '\n',
    'a'.repeat(15) + '\u0085',
  ])('rejects invalid length or control characters', (value) =>
    expect(registrationPassword(new FormControl(value))).not.toBeNull(),
  );
  it('accepts a short nonblank login password rather than applying registration policy', () =>
    expect(nonBlank(new FormControl('short'))).toBeNull());
  it.each([
    'not-email',
    'a@localhost',
    'a@-example.local',
    'a@exam ple.local',
    'a'.repeat(245) + '@example.local',
  ])('rejects invalid email input', (value) =>
    expect(emailAddress(new FormControl(value))).not.toBeNull(),
  );
  it('accepts a normalized local-domain email', () =>
    expect(emailAddress(new FormControl(' PERSON@EXAMPLE.LOCAL '))).toBeNull());
});
