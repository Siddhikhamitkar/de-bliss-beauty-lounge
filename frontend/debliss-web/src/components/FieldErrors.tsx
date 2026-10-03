/**
 * The messages the API returned for one field, taken from the `errors` map on
 * its 400 problem document. Renders nothing at all when the field is clean, so
 * a caller can place it under every input unconditionally.
 */
export function FieldErrors({ messages }: { messages: string[] | undefined }) {
  if (messages === undefined || messages.length === 0) {
    return null
  }

  return (
    <ul className="field-errors" role="alert">
      {messages.map(message => (
        <li key={message}>{message}</li>
      ))}
    </ul>
  )
}
