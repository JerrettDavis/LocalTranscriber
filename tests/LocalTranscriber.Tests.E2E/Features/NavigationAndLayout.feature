@server
Feature: Navigation and Layout
    As a user
    I want to switch between minimal and studio modes
    So that I can choose the interface that suits my workflow

Scenario: Minimal mode is the default view
    Given I am on the home page
    Then I should see minimal mode
    And I should be at the "capture" stage

Scenario: Switch from minimal to studio mode
    Given I am on the home page
    And I am in minimal mode
    When I switch to studio mode
    Then I should see the studio grid

Scenario: Switch from studio back to minimal mode
    Given I am on the home page
    When I switch to studio mode
    And I switch to minimal mode
    Then I should see minimal mode

Scenario: Studio mode shows 6 cards
    Given I am on the home page
    When I switch to studio mode
    Then I should see 6 studio cards

Scenario: Minimal mode starts at capture stage
    Given I am on the home page
    Then I should be at the "capture" stage
    And the record button should be visible
