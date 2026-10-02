import React from 'react'
import { createRoot } from 'react-dom/client'
import { MantineProvider, createTheme } from '@mantine/core'
import '@mantine/core/styles.css'
import ExamWindow from './ExamWindow.jsx'
import './styles.css'

// The exam runs in its own native window. It shares the same look as the app
// window so a test feels like one product.
const theme = createTheme({
  primaryColor: 'ieltop',
  defaultRadius: 'sm',
  fontFamily: '"Segoe UI", system-ui, -apple-system, "Helvetica Neue", Arial, sans-serif',
  colors: {
    ieltop: [
      '#fdecec', '#fbd5d5', '#f3aeae', '#ea8383', '#e35f5f',
      '#df4747', '#c1121f', '#a30f1a', '#8a0c16', '#6f0911',
    ],
  },
})

createRoot(document.getElementById('root')).render(
  <React.StrictMode>
    <MantineProvider theme={theme} defaultColorScheme="light">
      <ExamWindow />
    </MantineProvider>
  </React.StrictMode>
)
