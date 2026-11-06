import { HttpErrorResponse } from '@angular/common/http';
import { demoError } from './demo-error';

describe('Safe demo messages', () => {
  it.each([
    [409, 'demo_capacity_reached'], [409, 'demo_busy'], [403, 'demo_registration_disabled'],
  ])('uses local text for %s/%s', (status, code) => {
    const message = demoError(new HttpErrorResponse({ status: status as number, error: { code, error: 'UNTRUSTED_PROVIDER_TEXT' } }));
    expect(message).not.toBeNull();
    expect(message).not.toContain('UNTRUSTED_PROVIDER_TEXT');
  });
  it('preserves normal errors and requires the matching status', () => {
    expect(demoError(new HttpErrorResponse({ status: 400, error: { code: 'demo_capacity_reached' } }))).toBeNull();
    expect(demoError(new Error())).toBeNull();
  });
});
