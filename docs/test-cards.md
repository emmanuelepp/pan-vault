# Test cards

Every example in this repository, including those in the documentation and in the
automated tests, uses exclusively test card numbers published by the card brands
and by payment processors.

> **None of these numbers corresponds to a real account.** They are not issued,
> have no funds associated with them, and identify no person. They are valid under
> the Luhn algorithm precisely so that validation logic can be exercised without
> touching real data.
>
> **Never use a real PAN in this project**, not even your own. It is a
> demonstration system with no authentication, no encrypted persistence on disk
> and no audit trail. Introducing a real PAN would bring it into PCI DSS scope.

## Numbers used

| PAN | Brand | Length | Luhn | Use in the repository |
|-----|-------|--------|------|------------------------|
| `4111111111111111` | Visa | 16 | valid | Primary case in all examples |
| `4111111111111112` | Visa | 16 | **invalid** | Verifying Luhn rejection |
| `5555555555554444` | Mastercard | 16 | valid | Range 51 to 55 |
| `5105105105105100` | Mastercard | 16 | valid | Range 51 to 55 |
| `2223003122003222` | Mastercard | 16 | valid | Series 2, issued since 2017 |
| `2221000000000009` | Mastercard | 16 | valid | Lower bound of series 2 |
| `2720999999999996` | Mastercard | 16 | valid | Upper bound of series 2 |
| `378282246310005` | American Express | 15 | valid | Length other than 16 |
| `341111111111111` | American Express | 15 | valid | Prefix 34 |
| `3528000700000000` | JCB | 16 | valid | Distinguishing JCB from Amex |
| `3589000000000000` | JCB | 16 | valid | Upper bound of the JCB range |
| `36227206271667` | Diners Club | 14 | valid | Length of 14 |
| `30569309025904` | Diners Club | 14 | valid | Prefix 30 |
| `38520000023237` | Diners Club | 14 | valid | Prefix 38 |
| `6011111111111117` | Discover | 16 | valid | Prefix 6011 |
| `6511111111111119` | Discover | 16 | valid | Prefix 65 |
| `6212345678901232` | UnionPay | 16 | valid | Distinguishing UnionPay from Discover |

## Why this variety

Brand detection by IIN range is easy to get wrong. The common mistake is looking
only at the first digit, which produces three concrete confusions:

- `3` covers American Express (34, 37), Diners Club (30, 36, 38, 39) **and** JCB
  (3528 to 3589). Looking at the first digit lumps all three under the same brand
- `6` covers Discover (6011, 644 to 649, 65) and UnionPay (62)
- Mastercard uses 51 to 55, but also the 2221 to 2720 range since 2017. That
  second range starts with `2` and is missed entirely if only the first digit is
  checked

The boundary cases in the table (`2221...`, `2720...`, `3528...`, `3589...`) exist
to pin down the edges of each range, not just the middle.

## Lengths

A valid PAN has between 12 and 19 digits. Most of the examples are 16, but the
table deliberately includes 15 (Amex) and 14 (Diners) so that the masking and
validation functions do not become coupled to a fixed length.

`PanMasker.Mask` shows at most the first six and the last four digits, and fully
masks any input shorter than 12 characters rather than partially revealing it.

## Sensitive authentication data

This repository contains **no** verification codes (CVV, CVC2, CAV2, CID), PINs,
PIN blocks or magnetic stripe data, not even test values.

The service rejects them explicitly, so the examples that exercise that control
use obviously fake values such as `"123"` or `"1234"`, and they appear only in
requests whose expected result is a `400`.

See control 2 in [`pci-dss-v4-mapping.md`](pci-dss-v4-mapping.md).

## Sources

These numbers are public and appear in the testing documentation of the major
payment processors:

- Stripe: <https://docs.stripe.com/testing>
- Adyen: <https://docs.adyen.com/development-resources/testing/test-card-numbers/>
- PayPal: <https://developer.paypal.com/api/rest/sandbox/card-testing/>
- Braintree: <https://developer.paypal.com/braintree/docs/reference/general/testing>

If you add a new number, take it from one of those sources and add it to the table
with the reason you are including it.
