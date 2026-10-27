import { AbstractControl, FormControl, ValidationErrors, Validators } from '@angular/forms';

export function nonBlank(control: AbstractControl): ValidationErrors | null {
  return typeof control.value === 'string' && control.value.trim().length > 0
    ? null
    : { required: true };
}

export function emailAddress(control: AbstractControl): ValidationErrors | null {
  const value = typeof control.value === 'string' ? control.value.trim().toLowerCase() : '';
  if (!value) return { required: true };
  const domain = value.split('@')[1] ?? '';
  const labels = domain.split('.');
  if (
    value.length > 254 ||
    Validators.email(new FormControl(value)) ||
    labels.length < 2 ||
    labels.some((label) => !/^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$/.test(label))
  )
    return { email: true };
  return null;
}

export function registrationPassword(control: AbstractControl): ValidationErrors | null {
  if (nonBlank(control)) return { required: true };
  const value = control.value as string;
  // JS length, like .NET string.Length, counts UTF-16 code units. Never trim passwords.
  if (value.length < 15 || value.length > 128) return { length: true };
  return /[\u0000-\u001f\u007f-\u009f]/.test(value) ? { controlCharacter: true } : null;
}
