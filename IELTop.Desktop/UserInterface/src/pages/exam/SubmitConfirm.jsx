import { Modal, Text, Group, Button } from '@mantine/core'

/// Confirms submit, and warns when questions are still blank, like the real
/// test. The timer auto submit never shows this.
export default function SubmitConfirm({ open, unanswered, onCancel, onConfirm }) {
  return (
    <Modal opened={open} onClose={onCancel} title="Submit test" centered>
      <Text size="sm">
        {unanswered === 0
          ? 'Submit the test now?'
          : `Submit now? ${unanswered} question(s) have no answer.`}
      </Text>
      <Group justify="flex-end" mt="lg">
        <Button variant="default" onClick={onCancel}>
          Cancel
        </Button>
        <Button color="red" onClick={onConfirm}>
          Submit
        </Button>
      </Group>
    </Modal>
  )
}
