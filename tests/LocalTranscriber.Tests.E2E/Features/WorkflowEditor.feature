@server
Feature: Workflow Editor
    As a user
    I want to manage transcription workflows
    So that I can customize my transcription pipeline

Background:
    Given I am on the home page
    And the settings panel is open
    And the workflow editor is expanded

Scenario: Default workflow is loaded on startup
    Then the default workflow should be selected

Scenario: Duplicate workflow creates a copy
    Given the default workflow is selected
    When I duplicate the workflow
    Then the workflow select should have more options than before

Scenario: Create new empty workflow
    When I create a new workflow
    Then the workflow select should have more options than before

Scenario: Delete non-default workflow
    When I create a new workflow
    And I delete the current workflow
    Then the workflow select should have the original option count

Scenario: Cannot delete the default workflow
    Given the default workflow is selected
    Then the delete button should not be visible

Scenario: Add a step to the workflow
    Given I note the current step count
    When I add a "Transcribe" step
    Then the step count should have increased by 1

Scenario: Remove a step from the workflow
    Given I note the current step count
    And there is at least one step
    When I remove the first step
    Then the step count should have decreased by 1

Scenario: Reorder steps by moving down
    Given there are at least 2 steps
    And I note the name of step 0
    When I move step 0 down
    Then step 1 should have the previously noted name

Scenario: Configure step options
    Given there is at least one step
    When I click on step 0 header
    Then the step 0 config should be visible

Scenario: Switch between simple and phase view
    When I switch to "Phase" view
    Then the phase view should be active
    When I switch to "Simple" view
    Then the simple view should be active

Scenario: Reorder steps by moving up
    Given there are at least 2 steps
    And I note the name of step 1
    When I move step 1 up
    Then step 0 should have the previously noted name

Scenario: Collapse and re-expand the workflow editor
    When I collapse the workflow editor
    Then the workflow editor should be collapsed
    When I expand the workflow editor
    Then the workflow editor should be expanded

Scenario: Select a different workflow after duplicating
    Given the default workflow is selected
    When I duplicate the workflow
    Then the workflow select should have more options than before
    When I select the first workflow
    Then the default workflow should be selected

Scenario: Create workflow from template preset
    When I click the template button
    And I select the first preset
    Then the workflow should have steps
