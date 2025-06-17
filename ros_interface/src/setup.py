# ros_interface/setup.py
from setuptools import find_packages, setup

package_name = 'ros_interface'

setup(
    name=package_name,
    version='0.0.0',
    packages=find_packages(exclude=['test']),
    data_files=[
        ('share/' + package_name, ['package.xml']),
        # ('share/' + package_name + '/launch', ['launch/my_launch_file.launch.py']), # Example for launch files
    ],
    install_requires=['setuptools'],
    zip_safe=True,
    maintainer='Your Name',
    maintainer_email='your.email@example.com',
    description='TODO: Package description',
    license='TODO: License declaration',
    tests_require=['pytest'],
    entry_points={
        'console_scripts': [
            'ros_interface_node = ros_interface.ros_interface:main' # Your node entry point
        ],
    },
)
