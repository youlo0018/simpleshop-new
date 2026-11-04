export function money(value: number | string | null | undefined) {
  const amount = Number(value || 0);
  return Number.isInteger(amount) ? `¥${amount}` : `¥${amount.toFixed(2)}`;
}

export function dateTime(value?: string) {
  if (!value) return '';
  const normalized = value.includes('T') ? value : value.replace(' ', 'T') + 'Z';
  const date = new Date(normalized);
  return Number.isNaN(date.getTime())
    ? value
    : date.toLocaleString('zh-CN', { hour12: false });
}

export function maskPhone(phone?: string) {
  if (!phone) return '';
  return phone.replace(/^(\d{3})\d{4}(\d{4})$/, '$1****$2');
}
