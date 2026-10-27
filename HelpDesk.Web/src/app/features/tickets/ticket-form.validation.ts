import { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';

export function requiredTicketText(control: AbstractControl): ValidationErrors | null {
  // Unicode whitespace handled by .NET string.IsNullOrWhiteSpace, without trimming input.
  return typeof control.value !== 'string' ||
    /^[\u0009-\u000d\u0020\u0085\u00a0\u1680\u2000-\u200a\u2028\u2029\u202f\u205f\u3000]*$/u.test(
      control.value,
    )
    ? { required: true }
    : null;
}
export function enumSelection(values: readonly string[]): ValidatorFn {
  return (control) => (values.includes(control.value) ? null : { selection: true });
}
